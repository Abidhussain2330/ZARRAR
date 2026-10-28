using BURAQ.Models.Request.Edit;
using BURAQ.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Repositories;

public class GameChallengeRepository(
    AppDbContext context,
    IBlobRepository blobRepository
) : RepositoryBase(context),
    IGameChallengeRepository
{
    public async Task AddFlags(GameChallenge challenge, FlagCreateModel[] models, CancellationToken token = default)
    {
        foreach (var model in models)
        {
            var attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

            var flag = new FlagContext { Flag = model.Flag, Challenge = challenge, Attachment = attachment };
            challenge.Flags.Add(flag);
            if (challenge.Type == ChallengeType.DynamicAttachment)
                Context.VariantDatasetItems.Add(new VariantDatasetItem
                {
                    Challenge = challenge,
                    FlagContext = flag,
                    DatasetKey = Guid.NewGuid().ToString("N"),
                    ArtifactHash = attachment?.LocalFile?.Hash
                });
        }

        await SaveAsync(token);
    }

    public Task<bool> HasExistingFlags(GameChallenge challenge, IReadOnlyCollection<string> flags,
        CancellationToken token = default)
    {
        if (flags.Count == 0)
            return Task.FromResult(false);

        return Context.FlagContexts.AsNoTracking()
            .AnyAsync(f => f.ChallengeId == challenge.Id && flags.Contains(f.Flag), token);
    }

    public async Task<(int VariantCount, int RequiredCount)> GetDynamicAttachmentCapacity(int gameId,
        int challengeId, CancellationToken token = default)
    {
        var variantCount = await Context.FlagContexts.AsNoTracking()
            .CountAsync(f => f.ChallengeId == challengeId, token);
        var requiredCount = await Context.Participations.AsNoTracking()
            .CountAsync(p => p.GameId == gameId, token);

        return (variantCount, requiredCount);
    }

    public async Task<bool> HasIssuedVariants(int challengeId, CancellationToken token = default) =>
        await Context.VariantAssignments.AsNoTracking()
            .AnyAsync(a => a.ChallengeId == challengeId, token) ||
        await Context.VariantBuildJobs.AsNoTracking()
            .AnyAsync(j => j.Template.ChallengeId == challengeId, token);

    public Task<bool> HasPurchasedHints(int challengeId, CancellationToken token = default) =>
        Context.HintUnlocks.AsNoTracking().AnyAsync(unlock => unlock.ChallengeId == challengeId, token);

    public void AttachTestContainer(GameChallenge challenge, Container container)
    {
        // Container IDs are generated client-side. Without explicitly marking this new runtime
        // principal as Added, EF saves only TestContainerId on the tracked challenge and
        // PostgreSQL rejects the dangling foreign key.
        Context.Containers.Add(container);
        challenge.TestContainer = container;
    }

    public async Task<GameChallenge> CreateChallenge(Game game, GameChallenge challenge,
        CancellationToken token = default)
    {
        await Context.AddAsync(challenge, token);
        game.Challenges.Add(challenge);
        await SaveAsync(token);
        return challenge;
    }

    public async Task<bool> EnsureInstances(GameChallenge challenge, Game game, CancellationToken token = default)
    {
        var newInstances = Context.Participations
            .Where(p => p.GameId == game.Id && !Context.Set<GameInstance>()
                .Where(gi => gi.ChallengeId == challenge.Id)
                .Select(gi => gi.ParticipationId).Contains(p.Id)
            )
            .Select(p => new GameInstance { ParticipationId = p.Id, ChallengeId = challenge.Id })
            .ToList();

        if (newInstances.Count == 0)
            return false;

        await Context.Set<GameInstance>().AddRangeAsync(newInstances, token);
        await SaveAsync(token);

        return true;
    }

    public Task<GameChallenge?> GetChallenge(int gameId, int id, CancellationToken token = default)
        => Context.GameChallenges
            .Where(c => c.Id == id && c.GameId == gameId).FirstOrDefaultAsync(token);

    public Task LoadFlags(GameChallenge challenge, CancellationToken token = default) =>
        Context.Entry(challenge).Collection(c => c.Flags).LoadAsync(token);

    public Task<GameChallenge[]> GetChallenges(int gameId, CancellationToken token = default) =>
        Context.GameChallenges
            .Where(c => c.GameId == gameId).OrderBy(c => c.Id).ToArrayAsync(token);

    public Task<GameChallenge[]> GetChallengesWithTrafficCapturing(int gameId, CancellationToken token = default) =>
        Context.GameChallenges.IgnoreAutoIncludes().Where(c => c.GameId == gameId && c.EnableTrafficCapture)
            .ToArrayAsync(token);

    public async Task RemoveChallenge(GameChallenge challenge, bool save = true, CancellationToken token = default)
    {
        await blobRepository.DeleteAttachment(challenge.Attachment, token);

        await LoadFlags(challenge, token);

        // only dynamic attachment challenge's flag contexts have attachment
        if (challenge.Type == ChallengeType.DynamicAttachment)
            foreach (var flag in challenge.Flags)
                await blobRepository.DeleteAttachment(flag.Attachment, token);

        Context.RemoveRange(await Context.VariantDatasetItems
            .Where(x => x.ChallengeId == challenge.Id).ToArrayAsync(token));
        Context.RemoveRange(await Context.VariantTemplates
            .Where(x => x.ChallengeId == challenge.Id).ToArrayAsync(token));
        Context.RemoveRange(challenge.Flags);
        Context.Remove(challenge);

        if (save)
            await SaveAsync(token);
    }

    public async Task<TaskStatus> RemoveFlag(GameChallenge challenge, int flagId, CancellationToken token = default)
    {
        var flag = await Context.FlagContexts
            .Include(f => f.VariantDatasetItem)
            .FirstOrDefaultAsync(f => f.Challenge == challenge && f.Id == flagId, token);

        if (flag is null)
            return TaskStatus.NotFound;

        // Once a personalized flag/artifact has been issued, deleting the FlagContext would erase
        // ownership provenance (GameInstance.FlagId is SET NULL on delete) and can break a team that
        // already loaded the challenge. Issued variants are therefore immutable through normal flag
        // management. A future explicit administrative retirement flow can preserve assignment history.
        if (flag.IsOccupied || await Context.GameInstances.AsNoTracking()
                .AnyAsync(i => i.FlagId == flag.Id, token) ||
            await Context.VariantAssignments.AsNoTracking()
                .AnyAsync(a => a.FlagContextId == flag.Id, token))
            return TaskStatus.Denied;

        await blobRepository.DeleteAttachment(flag.Attachment, token);

        if (flag.VariantDatasetItem is not null)
            Context.Remove(flag.VariantDatasetItem);
        Context.Remove(flag);

        await SaveAsync(token);

        // If there are no more flags, disable the challenge
        if (!await Context.FlagContexts.AnyAsync(f => f.Challenge == challenge, token))
        {
            challenge.IsEnabled = false;
            await SaveAsync(token);
        }

        return TaskStatus.Success;
    }

    public async Task UpdateAttachment(GameChallenge challenge, AttachmentCreateModel model,
        CancellationToken token = default)
    {
        var attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

        await blobRepository.DeleteAttachment(challenge.Attachment, token);

        if (attachment is not null)
            await Context.AddAsync(attachment, token);

        challenge.Attachment = attachment;

        await SaveAsync(token);
    }
}
