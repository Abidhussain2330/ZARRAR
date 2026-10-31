using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using ZARRAR.Middlewares;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Cache;
using ZARRAR.Services.Container.Manager;
using ZARRAR.Services.Soc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ZARRAR.Controllers;

/// <summary>Administrator-only Security Operations Center APIs.</summary>
[RequireAdmin]
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class SocController(AppDbContext db, ISocEventPublisher events, ResourceMonitoringService monitoring,
    IContainerRepository containers, IOptionsSnapshot<ZARRAR.Models.Internal.ContainerPolicy> containerPolicy,
    IConfiguration configuration, UserManager<UserInfo> userManager,
    IParticipationRepository participationRepository, CacheHelper cacheHelper) : ControllerBase
{
    private readonly byte[] _fingerprintKey =
        (configuration["Soc:FingerprintKey"] is { Length: > 0 } fk ? fk : configuration["XorKey"])?.ToUTF8Bytes() ?? [];

    [HttpGet("Overview")]
    public async Task<ActionResult<SocOverviewModel>> Overview([FromQuery] string range = "1h",
        [FromQuery] int? gameId = null, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now - ParseRange(range);

        var participationQuery = db.Participations.AsNoTracking().Where(p =>
            p.Status == ParticipationStatus.Accepted && p.Game.StartTimeUtc <= now && p.Game.EndTimeUtc >= now);
        var containerQuery = db.Containers.AsNoTracking().Where(c =>
            c.GameInstanceId != null && c.Status != ContainerStatus.Destroyed);
        var submissionQuery = db.Submissions.AsNoTracking().AsQueryable();
        var solveQuery = db.FirstSolves.AsNoTracking().AsQueryable();
        var alertQuery = db.SocAlerts.AsNoTracking().AsQueryable();
        var riskCaseQuery = db.SocRiskCases.AsNoTracking().AsQueryable();

        if (gameId is not null)
        {
            participationQuery = participationQuery.Where(p => p.GameId == gameId);
            containerQuery = containerQuery.Where(c => c.GameInstance!.Participation.GameId == gameId);
            submissionQuery = submissionQuery.Where(s => s.GameId == gameId);
            solveQuery = solveQuery.Where(s => s.Participation.GameId == gameId);
            alertQuery = alertQuery.Where(a => a.GameId == gameId);
            riskCaseQuery = riskCaseQuery.Where(x => x.GameId == gameId);
        }

        // Activity counts follow the selected range (they used to be all-time totals, which was
        // both misleading next to a "last hour" chart and an ever-growing scan).
        submissionQuery = submissionQuery.Where(s => s.SubmitTimeUtc >= since);
        solveQuery = solveQuery.Where(s => s.Submission.SubmitTimeUtc >= since);

        var activeTeams = await participationQuery.CountAsync(token);
        var onlineUsers = await db.Users.AsNoTracking().CountAsync(u => u.LastVisitedUtc >= now.AddMinutes(-5), token);
        var activeContainerIds = await containerQuery.Select(c => c.Id).ToArrayAsync(token);
        var submissions = await submissionQuery.CountAsync(token);
        var solves = await solveQuery.CountAsync(token);
        var failed = await submissionQuery.CountAsync(s =>
            s.Status == AnswerResult.WrongAnswer || s.Status == AnswerResult.NotFound ||
            s.Status == AnswerResult.CheatDetected, token);
        var openAlerts = await alertQuery.CountAsync(a =>
            a.Status == SocAlertStatus.Open || a.Status == SocAlertStatus.Investigating ||
            a.Status == SocAlertStatus.Escalated, token);
        var critical = await alertQuery.CountAsync(a => a.Severity == SocSeverity.Critical &&
            a.Status != SocAlertStatus.Resolved && a.Status != SocAlertStatus.Clean, token);
        var openRiskCases = await riskCaseQuery.CountAsync(x => x.Status == SocAlertStatus.Open ||
            x.Status == SocAlertStatus.Investigating || x.Status == SocAlertStatus.Escalated, token);
        var metrics = monitoring.ContainerMetrics
            .Where(x => gameId is null || activeContainerIds.Contains(x.ContainerGuid)).ToArray();
        var failedInstances = metrics.Count(x => x.RuntimeStatus == "Unavailable");

        return new SocOverviewModel(activeTeams, onlineUsers, activeContainerIds.Length, submissions, solves, failed,
            openAlerts, critical, openRiskCases, failedInstances, ToProviderModel(monitoring.Provider), monitoring.Host,
            monitoring.HistorySince(since), metrics);
    }

    [HttpGet("Instances")]
    public async Task<ActionResult<ArrayResponse<SocInstanceModel>>> Instances([FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0, [FromQuery] string? search = null,
        [FromQuery] string? status = null, [FromQuery] int? teamId = null, [FromQuery] int? gameId = null,
        [FromQuery] int? challengeId = null, CancellationToken token = default)
    {
        var query = db.Containers.IgnoreAutoIncludes().AsNoTracking().Where(c => c.GameInstance != null);
        if (teamId is not null) query = query.Where(c => c.GameInstance!.Participation.TeamId == teamId);
        if (gameId is not null) query = query.Where(c => c.GameInstance!.Participation.GameId == gameId);
        if (challengeId is not null) query = query.Where(c => c.GameInstance!.ChallengeId == challengeId);
        if (Enum.TryParse<ContainerStatus>(status, true, out var parsed)) query = query.Where(c => c.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var hint = search.Trim().ToLower();
            query = query.Where(c => c.ContainerId.ToLower().Contains(hint) ||
                c.GameInstance!.Participation.Team.Name.ToLower().Contains(hint) ||
                c.GameInstance.Challenge.Title.ToLower().Contains(hint));
        }

        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(c => c.StartedAt).Skip(skip).Take(count)
            .Select(c => new { c.Id, c.ContainerId, c.StartedAt, c.ExpectStopAt, c.Status,
                TeamId = c.GameInstance!.Participation.TeamId, Team = c.GameInstance.Participation.Team.Name,
                GameId = c.GameInstance.Participation.GameId, Game = c.GameInstance.Participation.Game.Title,
                ChallengeId = c.GameInstance.ChallengeId, Challenge = c.GameInstance.Challenge.Title,
                NetworkMode = c.GameInstance.Challenge.NetworkMode, c.GameInstance.Challenge.CPUCount,
                c.GameInstance.Challenge.MemoryLimit }).ToArrayAsync(token);
        var metricMap = monitoring.ContainerMetrics.ToDictionary(x => x.ContainerGuid);
        var providerNode = monitoring.Provider.Node;
        var result = rows.Select(c =>
        {
            metricMap.TryGetValue(c.Id, out var metric);
            return new SocInstanceModel(c.Id, c.ContainerId, c.TeamId, c.Team, c.GameId, c.Game,
                c.ChallengeId, c.Challenge, providerNode, c.StartedAt, c.ExpectStopAt,
                metric?.RuntimeStatus ?? c.Status.ToString(), c.NetworkMode?.ToString() ?? "Unknown",
                metric?.CpuPercent, metric?.MemoryUsedBytes, metric?.MemoryLimitBytes,
                metric?.NetworkRxBytes ?? 0, metric?.NetworkTxBytes ?? 0, metric?.RestartCount ?? 0,
                c.CPUCount, c.MemoryLimit);
        }).ToArray();
        return new ArrayResponse<SocInstanceModel>(result, total);
    }

    [HttpPost("Instances/{id:guid}/Extend")]
    public async Task<IActionResult> Extend(Guid id, CancellationToken token)
    {
        var container = await containers.GetContainerWithInstanceById(id, token);
        if (container is null) return NotFound();
        await containers.ExtendLifetime(container, TimeSpan.FromMinutes(containerPolicy.Value.ExtensionDuration), token);
        await AuditAction("InstanceExtended", container, token);
        return Ok();
    }

    [HttpDelete("Instances/{id:guid}")]
    public async Task<IActionResult> Destroy(Guid id, CancellationToken token)
    {
        var container = await containers.GetContainerWithInstanceById(id, token);
        if (container is null) return NotFound();
        await AuditAction("InstanceDestroyed", container, token);
        return await containers.DestroyContainer(container, token) ? Ok() : BadRequest();
    }

    [HttpGet("Alerts")]
    public async Task<ActionResult<ArrayResponse<SocAlertModel>>> Alerts([FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0, [FromQuery] string? category = null,
        [FromQuery] string? severity = null, [FromQuery] string? status = null, [FromQuery] int? teamId = null,
        [FromQuery] int? relatedTeamId = null, [FromQuery] int? gameId = null,
        [FromQuery] int? challengeId = null, CancellationToken token = default)
    {
        var query = db.SocAlerts.AsNoTracking().AsQueryable();
        if (Enum.TryParse<SocCategory>(category, true, out var cat)) query = query.Where(x => x.Category == cat);
        if (Enum.TryParse<SocSeverity>(severity, true, out var sev)) query = query.Where(x => x.Severity == sev);
        if (Enum.TryParse<SocAlertStatus>(status, true, out var stat)) query = query.Where(x => x.Status == stat);
        if (teamId is not null) query = query.Where(x => x.TeamId == teamId);
        if (relatedTeamId is not null) query = query.Where(x => x.RelatedTeamId == relatedTeamId);
        if (gameId is not null) query = query.Where(x => x.GameId == gameId);
        if (challengeId is not null) query = query.Where(x => x.ChallengeId == challengeId);
        var total = await query.CountAsync(token);
        var alerts = await query.OrderByDescending(x => x.CreatedAtUtc).Skip(skip).Take(count).ToArrayAsync(token);
        var names = await LoadNames(alerts.Select(x => x.TeamId).Concat(alerts.Select(x => x.RelatedTeamId)),
            alerts.Select(x => x.GameId), alerts.Select(x => x.ChallengeId), token);
        return new ArrayResponse<SocAlertModel>(alerts.Select(x => new SocAlertModel(x.Id, x.CreatedAtUtc,
            x.Severity.ToString(), x.Category.ToString(), x.Event, x.Description, x.TeamId,
            Name(names.Teams, x.TeamId), x.RelatedTeamId, Name(names.Teams, x.RelatedTeamId), x.GameId,
            Name(names.Games, x.GameId), x.ChallengeId, Name(names.Challenges, x.ChallengeId), x.InstanceId,
            x.RiskImpact, x.Status.ToString(), x.EvidenceFingerprint, x.Evidence, x.Notes)).ToArray(), total);
    }

    [HttpPatch("Alerts/{id:long}")]
    public async Task<IActionResult> UpdateAlert(long id, SocAlertUpdateModel model, CancellationToken token)
    {
        if (!Enum.TryParse<SocAlertStatus>(model.Status, true, out var status)) return BadRequest();
        // Same rule as cases: only an administrator can own an investigation.
        if (model.AssignedAdminId is not null && !await db.Users.AsNoTracking()
                .AnyAsync(x => x.Id == model.AssignedAdminId && x.Role == Role.Admin, token))
            return BadRequest(new RequestResponse("Assigned investigator must be an administrator.",
                StatusCodes.Status400BadRequest));
        if (model.Notes is { Length: > 1000 })
            return BadRequest(new RequestResponse("Notes are limited to 1000 characters.",
                StatusCodes.Status400BadRequest));

        var alert = await db.SocAlerts.SingleOrDefaultAsync(x => x.Id == id, token);
        if (alert is null) return NotFound();
        var previous = alert.Status;
        alert.Status = status; alert.Notes = model.Notes; alert.AssignedAdminId = model.AssignedAdminId;
        alert.UpdatedAtUtc = DateTimeOffset.UtcNow;
        // The audit row records the transition, so closing an alert to hide it is visible later.
        events.Enqueue(new SocEventMessage(
            EventType: "AlertStatusChanged",
            Category: SocCategory.PlatformSecurity,
            Severity: status is SocAlertStatus.Clean or SocAlertStatus.Resolved && alert.Severity >= SocSeverity.High
                ? SocSeverity.Medium
                : SocSeverity.Info,
            Details: $"Alert {id} changed from {previous} to {status} by an administrator.",
            Source: nameof(SocController),
            TeamId: alert.TeamId,
            GameId: alert.GameId,
            ChallengeId: alert.ChallengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                alertId = id, from = previous.ToString(), to = status.ToString(), alert.Severity,
                model.AssignedAdminId
            })));
        await db.SaveChangesAsync(token);
        return Ok();
    }

    [HttpGet("Cases")]
    public async Task<ActionResult<ArrayResponse<SocRiskCaseModel>>> Cases(
        [FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        [FromQuery][Range(0, 100)] int minRisk = 0,
        [FromQuery] string? status = null,
        [FromQuery] int? teamId = null,
        [FromQuery] int? gameId = null,
        CancellationToken token = default)
    {
        var query = db.SocRiskCases.AsNoTracking().AsQueryable();
        if (Enum.TryParse<SocAlertStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        if (teamId is not null) query = query.Where(x => x.PrimaryTeamId == teamId || x.RelatedTeamId == teamId);
        if (gameId is not null) query = query.Where(x => x.GameId == gameId);
        if (minRisk > 0) query = query.Where(x => x.RiskScore >= minRisk);

        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.RiskScore).ThenByDescending(x => x.LastSeenAtUtc)
            .Skip(skip).Take(count)
            .Select(x => new
            {
                x.Id, x.CaseKey, x.Title, x.Summary, x.Status, x.RiskScore, x.Confidence,
                x.RuleHitCount, x.EvidenceCount, x.FirstSeenAtUtc, x.LastSeenAtUtc, x.UpdatedAtUtc,
                x.GameId, Game = x.Game.Title, x.PrimaryTeamId, PrimaryTeam = x.PrimaryTeam.Name,
                x.RelatedTeamId, RelatedTeam = x.RelatedTeam != null ? x.RelatedTeam.Name : null,
                x.AssignedAdminId, x.Notes
            }).ToArrayAsync(token);
        // Show each case's *current* risk (evidence decays with age) and order the page by it.
        var current = await CurrentRiskAsync(rows.Select(x => x.Id).ToArray(), token);
        var result = rows.Select(x => new SocRiskCaseModel(x.Id, x.CaseKey, x.Title, x.Summary,
                x.Status.ToString(), current.GetValueOrDefault(x.Id, x.RiskScore), x.Confidence, x.RuleHitCount,
                x.EvidenceCount, x.FirstSeenAtUtc, x.LastSeenAtUtc, x.UpdatedAtUtc, x.GameId, x.Game,
                x.PrimaryTeamId, x.PrimaryTeam, x.RelatedTeamId, x.RelatedTeam, x.AssignedAdminId, x.Notes))
            .OrderByDescending(x => x.RiskScore).ThenByDescending(x => x.LastSeenAt)
            .ToArray();
        return new ArrayResponse<SocRiskCaseModel>(result, total);
    }

    private async Task<Dictionary<Guid, int>> CurrentRiskAsync(Guid[] caseIds, CancellationToken token)
    {
        if (caseIds.Length == 0)
            return new Dictionary<Guid, int>();

        var evidence = await db.SocRiskEvidence.AsNoTracking()
            .Where(x => caseIds.Contains(x.RiskCaseId))
            .Select(x => new { x.RiskCaseId, x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc })
            .ToArrayAsync(token);
        var now = DateTimeOffset.UtcNow;

        return evidence.GroupBy(x => x.RiskCaseId).ToDictionary(g => g.Key,
            g => SocRiskScoring.CalculateAt(g.Select(x => (x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc)), now)
                .Risk);
    }

    [HttpGet("Cases/{id:guid}")]
    public async Task<ActionResult<SocRiskCaseDetailModel>> CaseDetail(Guid id,
        CancellationToken token = default)
    {
        var item = await db.SocRiskCases.AsNoTracking().Include(x => x.Evidence)
            .Include(x => x.Game).Include(x => x.PrimaryTeam).Include(x => x.RelatedTeam)
            .SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();

        var model = ToModel(item) with
        {
            RiskScore = SocRiskScoring.CalculateAt(item.Evidence.Select(x =>
                (x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc)), DateTimeOffset.UtcNow).Risk
        };
        var evidence = item.Evidence.OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new SocRiskEvidenceModel(x.Id, x.EvidenceKey, x.Kind, x.Weight, x.Confidence,
                x.OccurredAtUtc, x.TeamId, x.RelatedTeamId, x.ChallengeId, x.SubmissionId,
                x.AlertId, x.Fingerprint, x.MetadataJson)).ToArray();
        return new SocRiskCaseDetailModel(model, evidence);
    }

    [HttpPatch("Cases/{id:guid}")]
    public async Task<IActionResult> UpdateCase(Guid id, SocRiskCaseUpdateModel model, CancellationToken token)
    {
        if (!Enum.TryParse<SocAlertStatus>(model.Status, true, out var status)) return BadRequest();
        if (model.AssignedAdminId is not null && !await db.Users.AsNoTracking()
                .AnyAsync(x => x.Id == model.AssignedAdminId && x.Role == Role.Admin, token))
            return BadRequest(new RequestResponse("Assigned investigator must be an administrator.",
                StatusCodes.Status400BadRequest));

        var item = await db.SocRiskCases.SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        var previous = item.Status;
        item.Status = status;
        item.Notes = model.Notes;
        item.AssignedAdminId = model.AssignedAdminId;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Alert fatigue: the case's alerts that nobody has handled follow the case, so triaging
        // the case clears its whole burst of alerts instead of leaving them one by one.
        var alertIds = await db.SocRiskEvidence.AsNoTracking()
            .Where(x => x.RiskCaseId == id && x.AlertId != null)
            .Select(x => x.AlertId!.Value)
            .ToArrayAsync(token);
        var cascaded = 0;
        if (alertIds.Length > 0)
        {
            var linked = await db.SocAlerts
                .Where(a => alertIds.Contains(a.Id) &&
                            (a.Status == SocAlertStatus.Open || a.Status == SocAlertStatus.Investigating))
                .ToArrayAsync(token);
            foreach (var alert in linked)
            {
                alert.Status = status;
                alert.UpdatedAtUtc = item.UpdatedAtUtc;
            }

            cascaded = linked.Length;
        }

        events.Enqueue(new SocEventMessage(
            EventType: "SocRiskCaseStatusChanged", Category: SocCategory.AntiCheat,
            Severity: status == SocAlertStatus.Escalated ? SocSeverity.High : SocSeverity.Info,
            Details: $"Risk case {id} changed from {previous} to {status} by an administrator ({cascaded} linked alert(s) updated).",
            Source: nameof(SocController), TeamId: item.PrimaryTeamId, RelatedTeamId: item.RelatedTeamId,
            GameId: item.GameId, CorrelationId: item.Id.ToString()));
        await db.SaveChangesAsync(token);
        return Ok();
    }

    /// <summary>What a case's response actions can target: its teams, their members and recent solves.</summary>
    [HttpGet("Cases/{id:guid}/Targets")]
    public async Task<ActionResult<SocCaseTargetsModel>> CaseTargets(Guid id, CancellationToken token)
    {
        var item = await db.SocRiskCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();

        var teamIds = CaseTeams(item);
        var parts = await db.Participations.AsNoTracking()
            .Where(p => p.GameId == item.GameId && teamIds.Contains(p.TeamId))
            .Select(p => new { p.Id, p.TeamId, Team = p.Team.Name, p.Status })
            .ToArrayAsync(token);
        var partIds = parts.Select(p => p.Id).ToArray();

        var members = await db.UserParticipations.AsNoTracking()
            .Where(up => partIds.Contains(up.ParticipationId))
            .Select(up => new { up.ParticipationId, up.UserId, up.User.UserName, up.User.Role })
            .ToArrayAsync(token);

        // Evidence-linked submissions first, then the teams' latest accepted ones.
        var evidenceSubmissions = await db.SocRiskEvidence.AsNoTracking()
            .Where(x => x.RiskCaseId == id && x.SubmissionId != null)
            .Select(x => x.SubmissionId!.Value)
            .Distinct()
            .ToArrayAsync(token);
        var submissions = await db.Submissions.AsNoTracking()
            .Where(s => s.GameId == item.GameId && teamIds.Contains(s.TeamId) &&
                        (evidenceSubmissions.Contains(s.Id) || s.Status == AnswerResult.Accepted))
            .OrderByDescending(s => evidenceSubmissions.Contains(s.Id)).ThenByDescending(s => s.SubmitTimeUtc)
            .Take(40)
            .Select(s => new
            {
                s.Id, s.TeamId, Team = s.Team!.Name, UserName = s.User != null ? s.User.UserName : null,
                Challenge = s.GameChallenge!.Title, s.SubmitTimeUtc, s.Status
            })
            .ToArrayAsync(token);
        var submissionIds = submissions.Select(s => s.Id).ToArray();
        var scored = await db.FirstSolves.AsNoTracking()
            .Where(fs => submissionIds.Contains(fs.SubmissionId))
            .Select(fs => fs.SubmissionId)
            .ToHashSetAsync(token);

        return new SocCaseTargetsModel(
            parts.Select(p => new SocCaseTeamTarget(p.TeamId, p.Team, p.Id, p.Status.ToString(),
                members.Where(m => m.ParticipationId == p.Id)
                    .Select(m => new SocCaseMemberTarget(m.UserId, m.UserName ?? "?", m.Role.ToString()))
                    .ToArray())).ToArray(),
            submissions.Select(s => new SocCaseSubmissionTarget(s.Id, s.TeamId, s.Team, s.UserName, s.Challenge,
                s.SubmitTimeUtc, s.Status.ToString(), scored.Contains(s.Id))).ToArray());
    }

    /// <summary>
    /// Respond from the case: suspend or reinstate a team, ban a player, end a player's sessions,
    /// void a scored submission, or add a note. Every target is re-checked against the case (its
    /// game and its teams), every action is written to the case notes and the SOC timeline.
    /// </summary>
    [HttpPost("Cases/{id:guid}/Actions")]
    public async Task<IActionResult> CaseAction(Guid id, SocCaseActionModel model, CancellationToken token)
    {
        var item = await db.SocRiskCases.SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();

        // Every punitive action needs a reason on record (disputes come later, after the match).
        if (model.Action is not ("reinstate-team" or "note") && (model.Note?.Trim().Length ?? 0) < 4)
            return BadRequest(new RequestResponse("Write a short reason first (at least 4 characters)."));

        var teamIds = CaseTeams(item);
        var actor = await userManager.GetUserAsync(User);
        var actorName = actor?.UserName ?? "admin";
        string summary;
        var severity = SocSeverity.High;
        int? actionTeamId = null;
        Guid? actionUserId = null;
        int? actionSubmissionId = null;

        switch (model.Action)
        {
            case "suspend-team" or "reinstate-team":
            {
                if (model.TeamId is not { } teamId || !teamIds.Contains(teamId))
                    return BadRequest(new RequestResponse("Pick one of this case's teams."));

                var participation = await db.Participations
                    .Include(p => p.Team).Include(p => p.Game)
                    .SingleOrDefaultAsync(p => p.GameId == item.GameId && p.TeamId == teamId, token);
                if (participation is null)
                    return BadRequest(new RequestResponse("That team is not in this case's game."));

                var suspend = model.Action == "suspend-team";
                await participationRepository.UpdateParticipationStatus(participation,
                    suspend ? ParticipationStatus.Suspended : ParticipationStatus.Accepted, token);
                await cacheHelper.FlushScoreboardCache(item.GameId, token);

                actionTeamId = teamId;
                summary = $"{(suspend ? "suspended" : "reinstated")} team {participation.Team.Name}";
                severity = suspend ? SocSeverity.High : SocSeverity.Medium;
                break;
            }
            case "ban-user" or "sign-out-user":
            {
                if (model.UserId is not { } userId || !await IsCaseMemberAsync(item, teamIds, userId, token))
                    return BadRequest(new RequestResponse("Pick a member of this case's teams."));

                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null)
                    return BadRequest(new RequestResponse("User not found."));
                if (user.Role >= Role.Monitor)
                    return BadRequest(new RequestResponse("Staff accounts can't be actioned from a case."));

                if (model.Action == "ban-user")
                {
                    user.Role = Role.Banned;
                    await userManager.UpdateAsync(user);
                }

                // Rotating the security stamp invalidates every existing session cookie at the next
                // validation (one minute; see IdentityExtension).
                await userManager.UpdateSecurityStampAsync(user);

                actionUserId = userId;
                summary = model.Action == "ban-user"
                    ? $"banned user {user.UserName} and ended their sessions"
                    : $"ended all sessions of {user.UserName}";
                severity = model.Action == "ban-user" ? SocSeverity.High : SocSeverity.Medium;
                break;
            }
            case "void-submission":
            {
                if (model.SubmissionId is not { } submissionId)
                    return BadRequest(new RequestResponse("Pick a submission."));

                var submission = await db.Submissions
                    .SingleOrDefaultAsync(s => s.Id == submissionId && s.GameId == item.GameId &&
                                               teamIds.Contains(s.TeamId), token);
                if (submission is null)
                    return BadRequest(new RequestResponse("That submission is not from this case's teams and game."));

                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var solves = await db.FirstSolves.Where(fs => fs.SubmissionId == submissionId).ToArrayAsync(token);
                db.FirstSolves.RemoveRange(solves);
                submission.Status = AnswerResult.CheatDetected;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                await cacheHelper.FlushScoreboardCache(item.GameId, token);

                actionTeamId = submission.TeamId;
                actionSubmissionId = submissionId;
                summary = solves.Length > 0
                    ? $"voided scored submission #{submissionId} (points removed)"
                    : $"voided submission #{submissionId}";
                break;
            }
            case "note":
                if (string.IsNullOrWhiteSpace(model.Note))
                    return BadRequest(new RequestResponse("Write the note first."));
                summary = "added a note";
                severity = SocSeverity.Info;
                break;
            default:
                return BadRequest(new RequestResponse("Unknown action."));
        }

        var line = $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}Z] {actorName}: {summary}" +
                   (string.IsNullOrWhiteSpace(model.Note) ? "" : $" — {model.Note.Trim()}");
        var notes = string.IsNullOrEmpty(item.Notes) ? line : $"{item.Notes}\n{line}";
        // Keep the newest lines when the 2000-character limit is reached.
        item.Notes = notes.Length <= 2000 ? notes : notes[^2000..];
        if (severity >= SocSeverity.High && item.Status is SocAlertStatus.Open or SocAlertStatus.Investigating)
            item.Status = SocAlertStatus.Escalated;
        else if (item.Status == SocAlertStatus.Open)
            item.Status = SocAlertStatus.Investigating;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var notePreview = model.Note is { Length: > 200 } ? model.Note[..200] : model.Note;
        events.Enqueue(new SocEventMessage(
            EventType: "SocCaseActionTaken", Category: SocCategory.AntiCheat, Severity: severity,
            Details: $"Response on risk case {id}: {summary}.",
            Source: nameof(SocController), TeamId: actionTeamId ?? item.PrimaryTeamId,
            RelatedTeamId: item.RelatedTeamId, UserId: actionUserId, GameId: item.GameId,
            SubmissionId: actionSubmissionId, CorrelationId: item.Id.ToString(),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                action = model.Action, model.TeamId, model.UserId, model.SubmissionId,
                note = notePreview
            })));
        await db.SaveChangesAsync(token);

        return Ok(new RequestResponse($"Done: {summary}.", StatusCodes.Status200OK));
    }

    /// <summary>
    /// S3 — verify the SOC ledger: recompute every seal's hash chain and report the first break
    /// (an edited, deleted or back-dated timeline row), plus how many recent rows are not sealed yet.
    /// </summary>
    [HttpGet("Ledger")]
    public async Task<ActionResult<SocLedgerStatusModel>> Ledger(CancellationToken token)
    {
        var key = SocLedger.DeriveKey(configuration);
        var seals = await db.SocEvents.AsNoTracking()
            .Where(e => e.EventType == SocLedger.SealEventType)
            .OrderBy(e => e.Id)
            .ToArrayAsync(token);
        var oldest = await db.SocEvents.AsNoTracking()
            .Where(e => e.EventType != SocLedger.SealEventType)
            .MinAsync(e => (long?)e.Id, token);

        string? expectedPrevious = null;
        long previousTo = 0;
        var verifiedEvents = 0;
        var verifiedSeals = 0;
        var pruned = 0;

        foreach (var seal in seals)
        {
            var p = SocLedger.ReadSeal(seal);
            if (p is null)
                return await BrokenAsync(seal, "A seal record is unreadable.");

            // Retention may have removed the oldest rows; their seals can't be re-checked.
            if (oldest is { } first && p.FromId < first)
            {
                pruned++;
                expectedPrevious = p.Head;
                previousTo = p.ToId;
                continue;
            }

            // The very first seal must start the chain; otherwise earlier seals were removed.
            if (expectedPrevious is null && p.PreviousHead != SocLedger.GenesisHead)
                return await BrokenAsync(seal, "The oldest seal does not start the chain (earlier seals were removed).");

            if (expectedPrevious is not null && p.PreviousHead != expectedPrevious)
                return await BrokenAsync(seal, "The seal does not continue the previous seal (a seal was removed or rewritten).");

            var rows = await db.SocEvents.AsNoTracking()
                .Where(e => e.Id > previousTo && e.Id <= p.ToId && e.EventType != SocLedger.SealEventType)
                .OrderBy(e => e.Id)
                .ToArrayAsync(token);

            if (rows.Length != p.Count || rows.Length == 0 || rows[0].Id != p.FromId)
                return await BrokenAsync(seal,
                    $"The sealed range {p.FromId}–{p.ToId} held {p.Count} events; it now holds {rows.Length} (rows were deleted or inserted).");

            var head = p.PreviousHead;
            foreach (var row in rows)
                head = SocLedger.Next(key, head, row);

            if (head != p.Head)
                return await BrokenAsync(seal,
                    $"Events {p.FromId}–{p.ToId} no longer match their seal (a row was edited), or the ledger key changed.");

            verifiedSeals++;
            verifiedEvents += rows.Length;
            expectedPrevious = p.Head;
            previousTo = p.ToId;

            if (verifiedEvents > 500_000)
                break; // bound the work; the newest seals are the ones that matter during an event
        }

        var unsealed = await db.SocEvents.AsNoTracking()
            .CountAsync(e => e.Id > previousTo && e.EventType != SocLedger.SealEventType, token);

        return new SocLedgerStatusModel(true, verifiedSeals, verifiedEvents, unsealed, null, null,
            seals.Length > 0 ? seals[^1].TimestampUtc : null, pruned);

        async Task<SocLedgerStatusModel> BrokenAsync(SocEvent seal, string problem)
        {
            events.Enqueue(new SocEventMessage(
                EventType: "SocLedgerBroken", Category: SocCategory.PlatformSecurity,
                Severity: SocSeverity.Critical,
                Details: $"SOC ledger verification failed at seal {seal.Id}: {problem}",
                Source: nameof(SocController)));
            await db.SaveChangesAsync(token);
            return new SocLedgerStatusModel(false, verifiedSeals, verifiedEvents, 0, seal.Id, problem,
                seal.TimestampUtc, pruned);
        }
    }

    /// <summary>Bulk status change for alerts (clear a burst in one go). Audited as one event.</summary>
    [HttpPatch("Alerts/Bulk")]
    public async Task<IActionResult> BulkUpdateAlerts(SocAlertBulkUpdateModel model, CancellationToken token)
    {
        if (!Enum.TryParse<SocAlertStatus>(model.Status, true, out var status)) return BadRequest();
        if (model.Ids.Length is 0 or > 200)
            return BadRequest(new RequestResponse("Select between 1 and 200 alerts."));

        var ids = model.Ids.Distinct().ToArray();
        var alerts = await db.SocAlerts.Where(a => ids.Contains(a.Id)).ToArrayAsync(token);
        var now = DateTimeOffset.UtcNow;
        foreach (var alert in alerts)
        {
            alert.Status = status;
            alert.UpdatedAtUtc = now;
        }

        events.Enqueue(new SocEventMessage(
            EventType: "AlertStatusBulkChanged", Category: SocCategory.PlatformSecurity,
            Severity: alerts.Any(a => a.Severity >= SocSeverity.High) &&
                      status is SocAlertStatus.Clean or SocAlertStatus.Resolved
                ? SocSeverity.Medium
                : SocSeverity.Info,
            Details: $"{alerts.Length} alert(s) changed to {status} by an administrator.",
            Source: nameof(SocController),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                to = status.ToString(), ids = alerts.Select(a => a.Id).Take(200).ToArray()
            })));
        await db.SaveChangesAsync(token);
        return Ok();
    }

    private static int[] CaseTeams(SocRiskCase item) =>
        item.RelatedTeamId is { } related ? [item.PrimaryTeamId, related] : [item.PrimaryTeamId];

    private Task<bool> IsCaseMemberAsync(SocRiskCase item, int[] teamIds, Guid userId, CancellationToken token) =>
        db.UserParticipations.AsNoTracking().AnyAsync(up =>
            up.UserId == userId && up.GameId == item.GameId && teamIds.Contains(up.TeamId), token);

    [HttpPost("Cases/{id:guid}/Recalculate")]
    public async Task<ActionResult<SocRiskCaseModel>> RecalculateCase(Guid id, CancellationToken token)
    {
        var item = await db.SocRiskCases.Include(x => x.Evidence).Include(x => x.Game)
            .Include(x => x.PrimaryTeam).Include(x => x.RelatedTeam)
            .SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        SocRiskCaseService.Recalculate(item);
        events.Enqueue(new SocEventMessage(
            EventType: "SocRiskCaseRecalculated", Category: SocCategory.AntiCheat,
            Severity: SocSeverity.Info, Details: $"Risk case {id} was recalculated by an administrator.",
            Source: nameof(SocController), TeamId: item.PrimaryTeamId, RelatedTeamId: item.RelatedTeamId,
            GameId: item.GameId, CorrelationId: item.Id.ToString()));
        await db.SaveChangesAsync(token);
        return ToModel(item);
    }

    [HttpGet("Teams")]
    public async Task<ActionResult<ArrayResponse<SocTeamRiskModel>>> Teams([FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0, [FromQuery] int? teamId = null,
        [FromQuery] int? gameId = null, CancellationToken token = default)
    {
        var teamQuery = db.Teams.AsNoTracking().AsQueryable();
        if (teamId is not null) teamQuery = teamQuery.Where(x => x.Id == teamId);
        if (gameId is not null) teamQuery = teamQuery.Where(x => x.Participations.Any(p => p.GameId == gameId));
        var total = await teamQuery.CountAsync(token);
        var teams = await teamQuery.OrderBy(x => x.Name).Skip(skip).Take(count)
            .Select(x => new { x.Id, x.Name }).ToArrayAsync(token);
        var ids = teams.Select(x => x.Id).ToArray();

        var alertQuery = db.SocAlerts.AsNoTracking().Where(x =>
            ((x.TeamId != null && ids.Contains(x.TeamId.Value)) ||
             (x.RelatedTeamId != null && ids.Contains(x.RelatedTeamId.Value))) &&
            x.Status != SocAlertStatus.Resolved && x.Status != SocAlertStatus.Clean);
        var riskCaseQuery = db.SocRiskCases.AsNoTracking().Where(x =>
            (ids.Contains(x.PrimaryTeamId) || (x.RelatedTeamId != null && ids.Contains(x.RelatedTeamId.Value))) &&
            x.Status != SocAlertStatus.Resolved && x.Status != SocAlertStatus.Clean);
        var submissionQuery = db.Submissions.AsNoTracking().Where(x => ids.Contains(x.TeamId));
        var solveQuery = db.FirstSolves.AsNoTracking().Where(x => ids.Contains(x.Participation.TeamId));
        var runningQuery = db.Containers.AsNoTracking().Where(x => x.GameInstance != null &&
            ids.Contains(x.GameInstance.Participation.TeamId) && x.Status == ContainerStatus.Running);

        if (gameId is not null)
        {
            alertQuery = alertQuery.Where(x => x.GameId == gameId);
            riskCaseQuery = riskCaseQuery.Where(x => x.GameId == gameId);
            submissionQuery = submissionQuery.Where(x => x.GameId == gameId);
            solveQuery = solveQuery.Where(x => x.Participation.GameId == gameId);
            runningQuery = runningQuery.Where(x => x.GameInstance!.Participation.GameId == gameId);
        }

        var alerts = await alertQuery.ToArrayAsync(token);
        var cases = await riskCaseQuery.ToArrayAsync(token);
        var submissions = await submissionQuery.GroupBy(x => x.TeamId)
            .Select(x => new { TeamId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, token);
        var solves = await solveQuery.GroupBy(x => x.Participation.TeamId)
            .Select(x => new { TeamId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, token);
        var running = await runningQuery.GroupBy(x => x.GameInstance!.Participation.TeamId)
            .Select(x => new { TeamId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, token);

        var result = teams.Select(team =>
        {
            var own = alerts.Where(x => x.TeamId == team.Id || x.RelatedTeamId == team.Id).ToArray();
            var ownCases = cases.Where(x => x.PrimaryTeamId == team.Id || x.RelatedTeamId == team.Id).ToArray();
            var risk = Math.Max(Math.Min(100, own.Sum(x => x.RiskImpact)),
                ownCases.Select(x => x.RiskScore).DefaultIfEmpty(0).Max());
            var reasons = ownCases.OrderByDescending(x => x.RiskScore).Take(3)
                .Select(x => $"{x.RiskScore}% case: {x.Title}")
                .Concat(own.OrderByDescending(x => x.RiskImpact).Take(5)
                    .Select(x => $"+{x.RiskImpact} {x.Event}"))
                .Distinct().Take(5).ToArray();
            return new SocTeamRiskModel(team.Id, team.Name, risk, RiskClass(risk), own.Length,
                own.Count(x => x.Severity == SocSeverity.Critical), submissions.GetValueOrDefault(team.Id),
                solves.GetValueOrDefault(team.Id), running.GetValueOrDefault(team.Id),
                reasons);
        }).OrderByDescending(x => x.RiskScore).ToArray();
        return new ArrayResponse<SocTeamRiskModel>(result, total);
    }

    [HttpGet("Teams/{teamId:int}/InstanceLimits")]
    public async Task<ActionResult<SocTeamInstanceLimitModel[]>> TeamInstanceLimits(int teamId,
        CancellationToken token = default)
    {
        var rows = await db.Participations.AsNoTracking()
            .Where(p => p.TeamId == teamId)
            .OrderByDescending(p => p.Game.StartTimeUtc)
            .Select(p => new
            {
                ParticipationId = p.Id,
                p.GameId,
                Game = p.Game.Title,
                DefaultLimit = p.Game.ContainerCountLimit,
                OverrideLimit = p.ContainerCountLimit,
                RunningInstances = p.Instances.Count(i =>
                    i.Container != null && i.Container.Status == ContainerStatus.Running)
            })
            .ToArrayAsync(token);

        return rows.Select(x => new SocTeamInstanceLimitModel(x.ParticipationId, x.GameId, x.Game,
            x.RunningInstances, x.DefaultLimit, x.OverrideLimit, x.OverrideLimit ?? x.DefaultLimit)).ToArray();
    }

    [HttpPatch("Participations/{participationId:int}/InstanceLimit")]
    public async Task<ActionResult<SocTeamInstanceLimitModel>> UpdateTeamInstanceLimit(int participationId,
        SocTeamInstanceLimitUpdateModel model, CancellationToken token)
    {
        var participation = await db.Participations
            .Include(p => p.Game)
            .Include(p => p.Team)
            .SingleOrDefaultAsync(p => p.Id == participationId, token);

        if (participation is null)
            return NotFound();

        participation.ContainerCountLimit = model.Limit;
        var effectiveLimit = model.Limit ?? participation.Game.ContainerCountLimit;
        var displayedLimit = effectiveLimit == 0 ? "unlimited" : effectiveLimit.ToString();

        events.Enqueue(new SocEventMessage(
            EventType: "TeamInstanceLimitChanged",
            Category: SocCategory.Infrastructure,
            Severity: SocSeverity.Info,
            Details: $"Administrator set the instance limit for team {participation.Team.Name} " +
                     $"in game {participation.Game.Title} to {displayedLimit}.",
            Source: nameof(SocController),
            TeamId: participation.TeamId,
            GameId: participation.GameId));

        await db.SaveChangesAsync(token);

        var running = await db.GameInstances.AsNoTracking().CountAsync(i =>
            i.ParticipationId == participation.Id && i.Container != null &&
            i.Container.Status == ContainerStatus.Running, token);

        return new SocTeamInstanceLimitModel(participation.Id, participation.GameId,
            participation.Game.Title, running, participation.Game.ContainerCountLimit,
            participation.ContainerCountLimit, effectiveLimit);
    }

    [HttpGet("Flags")]
    public async Task<ActionResult<ArrayResponse<SocFlagModel>>> Flags([FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0, [FromQuery] int? teamId = null,
        [FromQuery] int? gameId = null, [FromQuery] int? challengeId = null,
        [FromQuery][RegularExpression("[0-9a-f]{64}")] string? artifactHash = null,
        CancellationToken token = default)
    {
        var query = db.GameInstances.IgnoreAutoIncludes().AsNoTracking().Where(x => x.FlagContext != null);
        if (teamId is not null) query = query.Where(x => x.Participation.TeamId == teamId);
        if (gameId is not null) query = query.Where(x => x.Participation.GameId == gameId);
        if (challengeId is not null) query = query.Where(x => x.ChallengeId == challengeId);
        if (!string.IsNullOrWhiteSpace(artifactHash))
            query = query.Where(x => x.FlagContext!.VariantAssignment!.ArtifactHash == artifactHash);
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.Container!.StartedAt).Skip(skip).Take(count)
            .Select(x => new { Flag = x.FlagContext!.Flag, TeamId = x.Participation.TeamId,
                Team = x.Participation.Team.Name, GameId = x.Participation.GameId, Game = x.Participation.Game.Title,
                ChallengeId = x.ChallengeId, Challenge = x.Challenge.Title, InstanceId = x.ContainerId,
                GeneratedAt = x.Container != null ? (DateTimeOffset?)x.Container.StartedAt : null,
                Status = x.Container == null ? "Expired" : x.Container.Status.ToString(),
                AssignmentId = x.FlagContext!.VariantAssignment != null
                    ? (Guid?)x.FlagContext.VariantAssignment.AssignmentId : null,
                ArtifactHash = x.FlagContext.VariantAssignment != null
                    ? x.FlagContext.VariantAssignment.ArtifactHash : null,
                AssignedAt = x.FlagContext.VariantAssignment != null
                    ? (DateTimeOffset?)x.FlagContext.VariantAssignment.AssignedAtUtc : null,
                FirstDownloadedAt = x.FlagContext.VariantAssignment != null
                    ? x.FlagContext.VariantAssignment.FirstDownloadedAtUtc : null,
                LastDownloadedAt = x.FlagContext.VariantAssignment != null
                    ? x.FlagContext.VariantAssignment.LastDownloadedAtUtc : null,
                DownloadCount = x.FlagContext.VariantAssignment != null
                    ? x.FlagContext.VariantAssignment.DownloadCount : 0,
                Immutable = x.FlagContext.VariantAssignment != null }).ToArrayAsync(token);
        return new ArrayResponse<SocFlagModel>(rows.Select(x => new SocFlagModel(
            FlagFingerprint.Compute(x.Flag, _fingerprintKey), x.TeamId, x.Team, x.GameId, x.Game,
            x.ChallengeId, x.Challenge, x.InstanceId, x.GeneratedAt, x.Status,
            x.AssignmentId, x.ArtifactHash, x.AssignedAt, x.FirstDownloadedAt,
            x.LastDownloadedAt, x.DownloadCount, x.Immutable)).ToArray(), total);
    }

    [HttpGet("Timeline")]
    public async Task<ActionResult<ArrayResponse<SocTimelineModel>>> Timeline(
        [FromQuery][Range(1, 100)] int count = 50,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        [FromQuery] int? teamId = null,
        [FromQuery] int? relatedTeamId = null,
        [FromQuery] int? gameId = null,
        [FromQuery] int? challengeId = null,
        [FromQuery] Guid? actorUserId = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? source = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken token = default)
    {
        var query = db.SocEvents.AsNoTracking().AsQueryable();
        if (teamId is not null) query = query.Where(x => x.TeamId == teamId);
        if (relatedTeamId is not null) query = query.Where(x => x.RelatedTeamId == relatedTeamId);
        if (gameId is not null) query = query.Where(x => x.GameId == gameId);
        if (challengeId is not null) query = query.Where(x => x.ChallengeId == challengeId);
        if (actorUserId is not null) query = query.Where(x => x.ActorUserId == actorUserId);
        if (userId is not null) query = query.Where(x => x.UserId == userId);
        if (Enum.TryParse<SocCategory>(category, true, out var cat)) query = query.Where(x => x.Category == cat);
        if (Enum.TryParse<SocSeverity>(severity, true, out var sev)) query = query.Where(x => x.Severity == sev);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(x => x.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(x => x.Source == source);
        if (!string.IsNullOrWhiteSpace(correlationId)) query = query.Where(x => x.CorrelationId == correlationId);
        if (from is not null) query = query.Where(x => x.TimestampUtc >= from);
        if (to is not null) query = query.Where(x => x.TimestampUtc <= to);

        var total = await query.CountAsync(token);
        var eventRows = await query.OrderByDescending(x => x.TimestampUtc).Skip(skip).Take(count).ToArrayAsync(token);
        var names = await LoadNames(eventRows.Select(x => x.TeamId).Concat(eventRows.Select(x => x.RelatedTeamId)),
            eventRows.Select(x => x.GameId), eventRows.Select(x => x.ChallengeId), token);
        return new ArrayResponse<SocTimelineModel>(eventRows.Select(x => new SocTimelineModel(x.Id, x.TimestampUtc,
            x.Severity.ToString(), x.Category.ToString(), x.EventType, x.TeamId, Name(names.Teams, x.TeamId),
            x.GameId, Name(names.Games, x.GameId), x.ChallengeId, Name(names.Challenges, x.ChallengeId),
            x.InstanceId, x.Details, x.EventId, x.Source, x.CorrelationId, x.RelatedTeamId,
            Name(names.Teams, x.RelatedTeamId), x.ActorUserId, x.UserId)).ToArray(), total);
    }

    [HttpGet("Timeline/{id:long}")]
    public async Task<ActionResult<SocTimelineDetailModel>> TimelineDetail(long id, CancellationToken token = default)
    {
        var item = await db.SocEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        var names = await LoadNames([item.TeamId, item.RelatedTeamId], [item.GameId], [item.ChallengeId], token);
        return new SocTimelineDetailModel(item.Id, item.EventId, item.TimestampUtc, item.Severity.ToString(),
            item.Category.ToString(), item.EventType, item.Source, item.Details, item.CorrelationId,
            item.PayloadJson, item.TeamId, Name(names.Teams, item.TeamId), item.RelatedTeamId,
            Name(names.Teams, item.RelatedTeamId), item.ActorUserId, item.UserId, item.GameId,
            Name(names.Games, item.GameId),
            item.ChallengeId, Name(names.Challenges, item.ChallengeId), item.InstanceId, item.SubmissionId);
    }

    [HttpGet("Outbox")]
    public async Task<ActionResult<SocOutboxStatusModel>> Outbox(CancellationToken token = default)
    {
        var pendingQuery = db.SocEventOutbox.AsNoTracking().Where(x => x.ProcessedAtUtc == null);
        var pending = await pendingQuery.CountAsync(x => x.AttemptCount < 10, token);
        var retrying = await pendingQuery.CountAsync(x => x.AttemptCount > 0 && x.AttemptCount < 10, token);
        var deadLettered = await pendingQuery.CountAsync(x => x.AttemptCount >= 10, token);
        var oldestPending = await pendingQuery.OrderBy(x => x.OccurredAtUtc)
            .Select(x => (DateTimeOffset?)x.OccurredAtUtc).FirstOrDefaultAsync(token);
        var lastProcessed = await db.SocEventOutbox.AsNoTracking().Where(x => x.ProcessedAtUtc != null)
            .MaxAsync(x => x.ProcessedAtUtc, token);
        return new SocOutboxStatusModel(pending, retrying, deadLettered, oldestPending, lastProcessed);
    }

    [HttpGet("DetectionQueue")]
    public async Task<ActionResult<SocDetectionQueueStatusModel>> DetectionQueue(
        CancellationToken token = default)
    {
        var query = db.SocDetectionJobs.AsNoTracking();
        var pending = await query.CountAsync(x => x.Status == SocDetectionStatus.Pending, token);
        var running = await query.CountAsync(x => x.Status == SocDetectionStatus.Running, token);
        var failed = await query.CountAsync(x => x.Status == SocDetectionStatus.Failed, token);
        var oldestPending = await query.Where(x => x.Status == SocDetectionStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc).Select(x => (DateTimeOffset?)x.CreatedAtUtc).FirstOrDefaultAsync(token);
        var lastCompleted = await query.Where(x => x.Status == SocDetectionStatus.Completed)
            .MaxAsync(x => x.CompletedAtUtc, token);
        return new SocDetectionQueueStatusModel(pending, running, failed, oldestPending, lastCompleted);
    }

    [HttpGet("DetectionQueue/Jobs")]
    public async Task<ActionResult<ArrayResponse<SocDetectionJobModel>>> DetectionJobs(
        [FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        [FromQuery] string state = "failed",
        CancellationToken token = default)
    {
        var query = db.SocDetectionJobs.AsNoTracking().AsQueryable();
        if (Enum.TryParse<SocDetectionStatus>(state, true, out var parsed))
            query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).Skip(skip).Take(count).ToArrayAsync(token);
        return new ArrayResponse<SocDetectionJobModel>(rows.Select(x => new SocDetectionJobModel(
            x.Id, x.SubmissionId, x.Status.ToString(), x.AttemptCount, x.CreatedAtUtc,
            x.StartedAtUtc, x.CompletedAtUtc, x.LastError)).ToArray(), total);
    }

    [HttpPost("DetectionQueue/{id:long}/Retry")]
    public async Task<IActionResult> RetryDetection(long id, CancellationToken token)
    {
        var item = await db.SocDetectionJobs.SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        if (item.Status is SocDetectionStatus.Running or SocDetectionStatus.Completed)
            return Conflict(new RequestResponse("Only pending or failed detection jobs can be retried.",
                StatusCodes.Status409Conflict));
        item.Status = SocDetectionStatus.Pending;
        item.AttemptCount = 0;
        item.StartedAtUtc = null;
        item.CompletedAtUtc = null;
        item.LastError = null;
        events.Enqueue(new SocEventMessage(
            EventType: "SocDetectionRetryRequested", Category: SocCategory.AntiCheat,
            Severity: SocSeverity.Medium,
            Details: $"An administrator requested retry of SOC detection job {id}.",
            Source: nameof(SocController), SubmissionId: item.SubmissionId));
        await db.SaveChangesAsync(token);
        return Ok();
    }

    [HttpGet("Outbox/Entries")]
    public async Task<ActionResult<ArrayResponse<SocOutboxEntryModel>>> OutboxEntries(
        [FromQuery][Range(1, 100)] int count = 30,
        [FromQuery][Range(0, int.MaxValue)] int skip = 0,
        [FromQuery] string state = "dead",
        CancellationToken token = default)
    {
        var query = db.SocEventOutbox.AsNoTracking().AsQueryable();
        query = state.ToLowerInvariant() switch
        {
            "pending" => query.Where(x => x.ProcessedAtUtc == null && x.AttemptCount == 0),
            "retrying" => query.Where(x => x.ProcessedAtUtc == null && x.AttemptCount > 0 && x.AttemptCount < 10),
            "processed" => query.Where(x => x.ProcessedAtUtc != null),
            _ => query.Where(x => x.ProcessedAtUtc == null && x.AttemptCount >= 10)
        };

        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).Skip(skip).Take(count)
            .Select(x => new SocOutboxEntryModel(x.Id, x.EventId, x.OccurredAtUtc,
                x.ProcessedAtUtc != null ? "Processed" : x.AttemptCount >= 10 ? "DeadLettered" :
                x.AttemptCount > 0 ? "Retrying" : "Pending", x.AttemptCount, x.NextAttemptAtUtc,
                x.LastError, x.EventType, x.Source, x.Severity.ToString(), x.Category.ToString(),
                x.TeamId, x.RelatedTeamId, x.ActorUserId, x.UserId, x.GameId, x.ChallengeId,
                x.InstanceId, x.SubmissionId, x.CorrelationId))
            .ToArrayAsync(token);
        return new ArrayResponse<SocOutboxEntryModel>(rows, total);
    }

    [HttpPost("Outbox/{id:long}/Retry")]
    public async Task<IActionResult> RetryOutbox(long id, CancellationToken token)
    {
        var item = await db.SocEventOutbox.SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        if (item.ProcessedAtUtc is not null)
            return Conflict(new RequestResponse("Processed SOC events cannot be retried.",
                StatusCodes.Status409Conflict));

        item.AttemptCount = 0;
        item.NextAttemptAtUtc = DateTimeOffset.UtcNow;
        item.LastError = null;
        events.Enqueue(new SocEventMessage(
            EventType: "SocOutboxRetryRequested",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Medium,
            Details: $"An administrator requested retry of SOC outbox record {id}.",
            Source: nameof(SocController),
            CorrelationId: item.CorrelationId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                outboxId = item.Id,
                item.EventId,
                item.EventType,
                item.AttemptCount
            })));
        await db.SaveChangesAsync(token);
        return Ok();
    }

    private async Task AuditAction(string action, Container container, CancellationToken token)
    {
        events.Enqueue(new SocEventMessage(
            EventType: action,
            Category: SocCategory.Infrastructure,
            Severity: SocSeverity.Info,
            Details: $"Administrator performed {action} on container {container.ShortId}.",
            Source: nameof(SocController),
            TeamId: container.GameInstance?.Participation.TeamId,
            GameId: container.GameInstance?.Participation.GameId,
            ChallengeId: container.GameInstance?.ChallengeId,
            InstanceId: container.Id));
        await db.SaveChangesAsync(token);
    }

    private static SocRiskCaseModel ToModel(SocRiskCase item) => new(item.Id, item.CaseKey,
        item.Title, item.Summary, item.Status.ToString(), item.RiskScore, item.Confidence,
        item.RuleHitCount, item.EvidenceCount, item.FirstSeenAtUtc, item.LastSeenAtUtc,
        item.UpdatedAtUtc, item.GameId, item.Game.Title, item.PrimaryTeamId, item.PrimaryTeam.Name,
        item.RelatedTeamId, item.RelatedTeam?.Name, item.AssignedAdminId, item.Notes);
    private static SocProviderSnapshot ToProviderModel(ContainerProviderStatus item) => new(item.Provider,
        item.Node, item.Status, item.Capacity, item.Running, item.Pending, item.Unavailable,
        item.Isolation, item.Version, item.SampledAtUtc);

    private static string? Name(Dictionary<int, string> values, int? id) => id is null ? null : values.GetValueOrDefault(id.Value);
    private async Task<(Dictionary<int, string> Teams, Dictionary<int, string> Games, Dictionary<int, string> Challenges)> LoadNames(
        IEnumerable<int?> teamIds, IEnumerable<int?> gameIds, IEnumerable<int?> challengeIds, CancellationToken token)
    {
        var t = teamIds.OfType<int>().Distinct().ToArray(); var g = gameIds.OfType<int>().Distinct().ToArray();
        var c = challengeIds.OfType<int>().Distinct().ToArray();
        return (await db.Teams.AsNoTracking().Where(x => t.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, token),
            await db.Games.AsNoTracking().Where(x => g.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, token),
            await db.GameChallenges.AsNoTracking().Where(x => c.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, token));
    }
    private static string RiskClass(int score) => score switch { >= 80 => "Critical", >= 60 => "High", >= 40 => "Suspicious", >= 20 => "Low", _ => "Normal" };
    private static TimeSpan ParseRange(string range) => range.ToLowerInvariant() switch
    { "15m" => TimeSpan.FromMinutes(15), "6h" => TimeSpan.FromHours(6), "24h" => TimeSpan.FromHours(24),
      "7d" => TimeSpan.FromDays(7), _ => TimeSpan.FromHours(1) };
}
