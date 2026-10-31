using ZARRAR.Models.Internal;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Container.Manager;
using ZARRAR.Services.Soc;
using ZARRAR.Services.Variants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ZARRAR.Repositories;

public class GameInstanceRepository(
    AppDbContext context,
    IContainerManager service,
    ICheatInfoRepository cheatInfoRepository,
    IContainerRepository containerRepository,
    IGameEventRepository gameEventRepository,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    IOptionsSnapshot<ContainerProvider> containerProvider,
    DynamicFlagHmac dynamicFlags,
    VariantDatasetSelector variantDatasetSelector,
    ISocEventPublisher socEvents,
    ILogger<GameInstanceRepository> logger,
    IStringLocalizer<Program> localizer) : RepositoryBase(context), IGameInstanceRepository
{
    private const int DynamicAttachmentAllocationLockNamespace = 0x42555241; // "BURA"

    public Task<GameInstance[]> GetRunningInstances(Participation part, CancellationToken token = default) =>
        Context.GameInstances
            .Include(i => i.Challenge)
            .Include(i => i.Container)
            // Read-only projection for a status panel — no transaction and no tracking, unlike
            // GetInstance which exists to mutate the instance it returns.
            .AsNoTracking()
            .Where(i => i.ParticipationId == part.Id
                        && i.Container != null
                        && i.Container.Status == ContainerStatus.Running)
            .ToArrayAsync(token);

    public async Task<GameInstance?> GetInstance(Participation part, int challengeId, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        var instance = await Context.GameInstances
            .Include(i => i.FlagContext)
            .Where(e => e.ChallengeId == challengeId && e.Participation == part)
            .SingleOrDefaultAsync(token);

        if (instance is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_NoInstance), part.Id, challengeId],
                TaskStatus.NotFound,
                LogLevel.Warning);
            await transaction.RollbackAsync(token);
            return null;
        }

        var challenge = instance.Challenge;

        if (!challenge.IsEnabled)
        {
            await transaction.RollbackAsync(token);
            return null;
        }

        if (instance.IsLoaded)
        {
            await transaction.RollbackAsync(token);
            return instance;
        }

        try
        {
            switch (instance.Challenge.Type)
            {
                // dynamic flag dispatch
                case ChallengeType.DynamicContainer:
                    var flagInstanceId = Guid.NewGuid();
                    instance.FlagContext = new()
                    {
                        Challenge = challenge,
                        FlagInstanceId = flagInstanceId,
                        Flag = dynamicFlags.Generate(part.GameId, challenge.Id, part.TeamId, part.Id, flagInstanceId),
                        IsOccupied = true
                    };
                    break;
                case ChallengeType.DynamicAttachment:
                    // Serialize allocation per challenge. Without this lock, two concurrent first-open
                    // requests can both observe the same unoccupied FlagContext before either transaction
                    // commits, assigning one personalized artifact/flag to multiple participations.
                    await Context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0}, {1})",
                        [DynamicAttachmentAllocationLockNamespace, challenge.Id],
                        cancellationToken: token);

                    // A pre-built deterministic variant may already belong to this participation.
                    // Reusing its durable assignment makes first-open idempotent after retries or restarts.
                    var existingAssignment = await Context.VariantAssignments
                        .Include(e => e.FlagContext)
                        .Where(e => e.ChallengeId == challenge.Id && e.ParticipationId == part.Id &&
                            e.RetiredAtUtc == null)
                        .SingleOrDefaultAsync(token);
                    if (existingAssignment is not null)
                    {
                        existingAssignment.FlagContext.IsOccupied = true;
                        instance.FlagId = existingAssignment.FlagContextId;
                        break;
                    }

                    var datasetItem = await variantDatasetSelector.SelectAsync(
                        challenge.Id, part.Id, DateTimeOffset.UtcNow, token);

                    // Compatibility fallback for a legacy database before its dataset sync has run.
                    // New uploads and the Phase 05 migration always create dataset rows.
                    var flag = datasetItem?.FlagContext ?? await Context.FlagContexts
                        .Where(e => e.ChallengeId == challenge.Id && !e.IsOccupied)
                        .OrderBy(e => e.Id)
                        .FirstOrDefaultAsync(token);

                    if (flag is null)
                    {
                        logger.SystemLog(
                            StaticLocalizer[nameof(Resources.Program.InstanceRepository_DynamicFlagsNotEnough),
                                challenge.Title,
                                challenge.Id], TaskStatus.Failed,
                            LogLevel.Warning);
                        await transaction.RollbackAsync(token);
                        return null;
                    }

                    flag.IsOccupied = true;
                    instance.FlagId = flag.Id;
                    var assignment = new VariantAssignment
                    {
                        ChallengeId = challenge.Id,
                        ParticipationId = part.Id,
                        FlagContextId = flag.Id,
                        ArtifactHash = datasetItem?.ArtifactHash ?? flag.Attachment?.LocalFile?.Hash,
                        Mode = datasetItem is null ? "Pool" : "Dataset"
                    };
                    Context.VariantAssignments.Add(assignment);
                    socEvents.Enqueue(new SocEventMessage(
                        EventType: "PersonalizedArtifactAssigned",
                        Category: SocCategory.PlatformSecurity,
                        Severity: SocSeverity.Info,
                        Details: $"A personalized artifact was assigned to team {part.Team.Name}.",
                        Source: nameof(GameInstanceRepository),
                        TeamId: part.TeamId,
                        GameId: part.GameId,
                        ChallengeId: challenge.Id,
                        PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                        {
                            assignment.AssignmentId,
                            assignment.ArtifactHash,
                            assignment.Mode
                        })));
                    break;
            }

            // instance.FlagContext is null by default
            // static flag does not need to be dispatched

            instance.IsLoaded = true;
            await SaveAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unable to allocate game instance state for participation {ParticipationId} and challenge {ChallengeId}.",
                part.Id, challengeId);
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_GetInstanceFailed), part.Team.Name,
                    challenge.Title,
                    challenge.Id],
                TaskStatus.Failed, LogLevel.Warning);
            await transaction.RollbackAsync(token);
            return null;
        }

        return instance;
    }

    public Task<GameInstance?> GetInstanceForSubmission(Participation team, int challengeId,
        CancellationToken token = default)
        => Context.GameInstances.IgnoreAutoIncludes()
            .Include(i => i.Challenge)
            .Where(i => i.ParticipationId == team.Id && i.ChallengeId == challengeId)
            .SingleOrDefaultAsync(token);

    public async Task<TaskResult<Container>> CreateContainer(GameInstance gameInstance, Team team, UserInfo user,
        Game game, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(gameInstance.Challenge.ContainerImage) ||
            gameInstance.Challenge.ExposePort is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    gameInstance.Challenge.Title],
                TaskStatus.Denied, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        // A participation override lets SOC constrain one team without changing the whole game.
        // Null inherits the game setting and zero means unlimited.
        var containerLimit = gameInstance.Participation.ContainerCountLimit ?? game.ContainerCountLimit;

        if (containerLimit > 0)
        {
            if (containerPolicy.Value.AutoDestroyOnLimitReached)
            {
                var running = await Context.GameInstances
                    .Where(i => i.Participation == gameInstance.Participation && i.Container != null)
                    .OrderBy(i => i.Container!.StartedAt).ToListAsync(token);

                var first = running.FirstOrDefault();
                if (running.Count >= containerLimit && first is not null)
                {
                    logger.Log(
                        StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerAutoDestroy),
                            team.Name, first.Challenge.Title,
                            first.Container!.LogId],
                        user, TaskStatus.Success);
                    await containerRepository.DestroyContainer(running.First().Container!, token);
                }
            }
            else
            {
                var count = await Context.GameInstances.CountAsync(
                    i => i.Participation == gameInstance.Participation &&
                         i.Container != null, token);

                if (count >= containerLimit)
                    return new TaskResult<Container>(TaskStatus.Denied);
            }
        }

        if (gameInstance.Container is not null)
            return new TaskResult<Container>(TaskStatus.Success, gameInstance.Container);

        await Context.Entry(gameInstance).Reference(e => e.FlagContext).LoadAsync(token);

        var challenge = gameInstance.Challenge;
        var container = await service.CreateContainerAsync(new ContainerConfig
        {
            TeamId = team.Id.ToString(),
            UserId = user.Id,
            ChallengeId = gameInstance.ChallengeId,
            GameId = challenge.GameId,
            Flag = gameInstance.FlagContext?.Flag, // static challenge has no specific flag
            Image = challenge.ContainerImage,
            CPUCount = challenge.CPUCount ?? 1,
            MemoryLimit = challenge.MemoryLimit ?? 64,
            StorageLimit = challenge.StorageLimit ?? 256,
            NetworkMode = challenge.NetworkMode ?? containerProvider.Value.DefaultNetworkMode,
            EnableTrafficCapture = challenge.EnableTrafficCapture && game.IsActive,
            ExposedPort = challenge.ExposePort ??
                          throw new ArgumentException(
                              localizer[nameof(Resources.Program.InstanceRepository_InvalidPort)])
        }, token);

        if (container is null)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreationFailed),
                    gameInstance.Challenge.Title],
                TaskStatus.Failed, LogLevel.Warning);
            return new TaskResult<Container>(TaskStatus.Failed);
        }

        // update the ExpectStopAt with config
        container.ExpectStopAt = container.StartedAt.AddMinutes(containerPolicy.Value.DefaultLifetime);

        // Container IDs are generated client-side, so attaching one only through this existing
        // GameInstance navigation can make EF treat it as an existing principal. Track the runtime
        // explicitly before any repository below calls SaveChanges for its audit event.
        Context.Containers.Add(container);
        gameInstance.Container = container;
        gameInstance.LastContainerOperation = DateTimeOffset.UtcNow;

        socEvents.Enqueue(new SocEventMessage(
            EventType: "ChallengeRuntimeProvisioned",
            Category: SocCategory.Infrastructure,
            Severity: SocSeverity.Info,
            Details: $"{containerProvider.Value.Type} provisioned a challenge runtime for team {team.Name}.",
            Source: nameof(GameInstanceRepository),
            TeamId: team.Id,
            UserId: user.Id,
            GameId: challenge.GameId,
            ChallengeId: challenge.Id,
            InstanceId: container.Id,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                provider = containerProvider.Value.Type.ToString(),
                runtimeId = container.ContainerId,
                networkMode = (challenge.NetworkMode ?? containerProvider.Value.DefaultNetworkMode).ToString(),
                cpuTenths = challenge.CPUCount ?? 1,
                memoryMb = challenge.MemoryLimit ?? 64,
                storageMb = challenge.StorageLimit ?? 256
            })));

        await gameEventRepository.AddEvent(
            new()
            {
                Type = EventType.ContainerStart,
                GameId = gameInstance.Challenge.GameId,
                TeamId = gameInstance.Participation.TeamId,
                UserId = user.Id,
                Values = [gameInstance.Challenge.Id.ToString(), gameInstance.Challenge.Title]
            }, token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.InstanceRepository_ContainerCreated), team.Name,
                gameInstance.Challenge.Title,
                container.LogId], user,
            TaskStatus.Success);

        return new TaskResult<Container>(TaskStatus.Success, gameInstance.Container);
    }

    public async Task<bool> DestroyAllContainers(GameChallenge challenge, CancellationToken token = default)
    {
        var success = true;

        foreach (var container in await Context.GameInstances
                     .Include(i => i.Container)
                     .Where(i => i.Challenge == challenge && i.ContainerId != null)
                     .Select(i => i.Container)
                     .ToArrayAsync(token))
        {
            if (container is null)
                continue;

            success &= await containerRepository.DestroyContainer(container, token);
        }

        return success;
    }

    public async Task<CheatCheckInfo> CheckCheat(Submission submission, CancellationToken token = default)
    {
        CheatCheckInfo checkInfo = new();

        var instance = await Context.GameInstances
            .Include(i => i.Participation)
            .ThenInclude(i => i.Team)
            .Include(i => i.FlagContext)
            .Where(i => i.ChallengeId == submission.ChallengeId &&
                        i.ParticipationId != submission.ParticipationId &&
                        i.FlagContext != null && i.FlagContext.Flag == submission.Answer)
            .FirstOrDefaultAsync(token);

        if (instance is null)
            return checkInfo;

        var updateSub = await Context.Submissions.Where(s => s.Id == submission.Id).SingleAsync(token);

        var cheatInfo = await cheatInfoRepository.CreateCheatInfo(updateSub, instance, token);

        checkInfo = CheatCheckInfo.FromCheatInfo(cheatInfo);

        updateSub.Status = AnswerResult.CheatDetected;

        await SaveAsync(token);

        return checkInfo;
    }

    public async Task<VerifyResult> VerifyAnswer(Submission submission, CancellationToken token = default)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync(token);

        try
        {
            var instance = await Context.GameInstances.IgnoreAutoIncludes()
                .Include(i => i.FlagContext)
                .SingleOrDefaultAsync(i => i.ChallengeId == submission.ChallengeId &&
                                           i.ParticipationId == submission.ParticipationId, token);

            if (instance is null)
            {
                submission.Status = AnswerResult.NotFound;
                await transaction.RollbackAsync(token);
                return new(SubmissionType.Unaccepted, AnswerResult.NotFound);
            }

            var updateSub = await Context.Submissions
                .IgnoreAutoIncludes()
                .SingleAsync(s => s.Id == submission.Id, token);

            var challenge = await Context.GameChallenges
                .AsNoTracking()
                .Select(c => new { c.Id, c.Type, c.DisableBloodBonus, c.DeadlineUtc })
                .SingleAsync(c => c.Id == submission.ChallengeId, token);

            if (instance.FlagContext is null && challenge.Type.IsStatic())
            {
                updateSub.Status = await Context.FlagContexts.AsNoTracking()
                    .AnyAsync(
                        f => f.ChallengeId == submission.ChallengeId && f.Flag == submission.Answer,
                        token)
                    ? AnswerResult.Accepted
                    : AnswerResult.WrongAnswer;
            }
            else
            {
                updateSub.Status = instance.FlagContext?.Flag == submission.Answer
                    ? AnswerResult.Accepted
                    : AnswerResult.WrongAnswer;
            }

            if (updateSub.Status != AnswerResult.Accepted)
            {
                await SaveAsync(token);
                await transaction.CommitAsync(token);
                return new(SubmissionType.Unaccepted, updateSub.Status);
            }

            // Acquire a PostgresSQL advisory lock to prevent race conditions:
            // This lock ensures that only one concurrent submission for the same participation/challenge pair
            // can proceed past this point, preventing duplicate FirstSolve entries if multiple submissions
            // are processed at the same time. Without this, two submissions could both pass the check and
            // insert duplicate records.
            await Context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
                [updateSub.ParticipationId, updateSub.ChallengeId],
                cancellationToken: token);

            var alreadySolved = await Context.FirstSolves
                .AnyAsync(fs => fs.ParticipationId == submission.ParticipationId &&
                                fs.ChallengeId == submission.ChallengeId, token);

            if (alreadySolved)
            {
                await SaveAsync(token);
                await transaction.CommitAsync(token);
                return new(SubmissionType.Normal, updateSub.Status);
            }

            // ZARRAR showdown: only the first correct flag captures the challenge. Serialise
            // captures of the same challenge and turn a photo-finish second flag away.
            if (Services.Zarrar.ZarrarDuelService.IsDuelGame(submission.GameId))
            {
                await Context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
                    [Services.Zarrar.ZarrarDuelService.CaptureLockNamespace, updateSub.ChallengeId],
                    cancellationToken: token);

                var capturedByOther = await Context.FirstSolves
                    .AnyAsync(fs => fs.ChallengeId == submission.ChallengeId &&
                                    fs.ParticipationId != submission.ParticipationId, token);

                if (capturedByOther)
                {
                    updateSub.Status = AnswerResult.WrongAnswer;
                    await SaveAsync(token);
                    await transaction.CommitAsync(token);
                    return new(SubmissionType.Unaccepted, updateSub.Status);
                }
            }

            var participation = await Context.Participations
                .IgnoreAutoIncludes().AsNoTracking()
                .Include(p => p.Division)
                .ThenInclude(d => d!.ChallengeConfigs)
                .SingleAsync(p => p.Id == submission.ParticipationId, token);

            var time = await Context.Games.IgnoreAutoIncludes().AsNoTracking()
                .Where(g => g.Id == participation.GameId)
                .Select(g => new { g.StartTimeUtc, g.EndTimeUtc })
                .SingleAsync(token);

            // Check if submission is within game time window
            var withinGameWindow = updateSub.SubmitTimeUtc >= time.StartTimeUtc &&
                                   updateSub.SubmitTimeUtc < time.EndTimeUtc;

            // Check if submission is within challenge deadline (if deadline is set)
            var withinDeadline = !challenge.DeadlineUtc.HasValue ||
                                 updateSub.SubmitTimeUtc <= challenge.DeadlineUtc.Value;

            // Blood bonus is only awarded if submission is within both game window and deadline
            var hasBloodPermission = withinGameWindow && withinDeadline && !challenge.DisableBloodBonus &&
                                     HasPermission(participation.Division, GamePermission.GetBlood,
                                         submission.ChallengeId);

            var submissionType = SubmissionType.Normal;
            if (hasBloodPermission)
            {
                var bloodEligibleCount = await CountBloodEligibleSolves(submission.ChallengeId, time.StartTimeUtc,
                    time.EndTimeUtc, token);

                submissionType = bloodEligibleCount switch
                {
                    0 => SubmissionType.FirstBlood,
                    1 => SubmissionType.SecondBlood,
                    2 => SubmissionType.ThirdBlood,
                    _ => SubmissionType.Normal
                };
            }

            Context.FirstSolves.Add(new FirstSolve
            {
                ParticipationId = submission.ParticipationId,
                ChallengeId = submission.ChallengeId,
                SubmissionId = submission.Id
            });

            await SaveAsync(token);
            await transaction.CommitAsync(token);

            return new(submissionType, updateSub.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during answer verification for submission {SubmissionId}.",
                submission.Id);
            await transaction.RollbackAsync(token);
            throw;
        }
    }

    private Task<int> CountBloodEligibleSolves(int challengeId, DateTimeOffset start, DateTimeOffset end,
        CancellationToken token)
    {
        // First, get blood-eligible participation IDs for the challenge
        var eligibleParticipationIds =
            from participation in Context.Participations.AsNoTracking()
            join config in Context.Set<DivisionChallengeConfig>().AsNoTracking()
                    .Where(c => c.ChallengeId == challengeId)
                on participation.DivisionId equals config.DivisionId into configJoin
            from cfg in configJoin.DefaultIfEmpty()
            join division in Context.Divisions.AsNoTracking()
                on participation.DivisionId equals division.Id into divisionJoin
            from div in divisionJoin.DefaultIfEmpty()
            where participation.Status == ParticipationStatus.Accepted
                  && (cfg != null
                      ? cfg.Permissions.HasFlag(GamePermission.GetBlood)
                      : div == null || div.DefaultPermissions.HasFlag(GamePermission.GetBlood))
            select participation.Id;

        // Now, count FirstSolves for the challenge with eligible participations and time window
        return (
            from fs in Context.FirstSolves.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on fs.SubmissionId equals submission.Id
            where fs.ChallengeId == challengeId
                  && eligibleParticipationIds.Contains(fs.ParticipationId)
                  && submission.SubmitTimeUtc >= start
                  && submission.SubmitTimeUtc < end
            orderby submission.SubmitTimeUtc
            select fs.ParticipationId
        ).Take(4).CountAsync(token);
    }

    private static bool HasPermission(Division? division, GamePermission permission, int challengeId)
    {
        if (division is null)
            return true;

        var specific = division.ChallengeConfigs.FirstOrDefault(c => c.ChallengeId == challengeId);
        var permissions = specific?.Permissions ?? division.DefaultPermissions;
        return permissions.HasFlag(permission);
    }
}
