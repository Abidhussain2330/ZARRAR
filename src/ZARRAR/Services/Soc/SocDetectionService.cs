using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ZARRAR.Models.Request.Admin;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Soc;

/// <summary>Durably consumes submissions and correlates team, pair and competition-wide signals.</summary>
public sealed class SocDetectionService(
    ChannelReader<SocDetectionEvent> wakeReader,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SocDetectionService> logger) : BackgroundService
{
    // S4 — a dedicated, stable key (Soc:FingerprintKey) keeps fingerprints comparable across
    // restarts even where XorKey is generated per run (development); XorKey stays the fallback so
    // existing fingerprints keep matching in production.
    private readonly byte[] _fingerprintKey =
        (configuration["Soc:FingerprintKey"] is { Length: > 0 } fk ? fk : configuration["XorKey"])?.ToUTF8Bytes() ?? [];

    /// <summary>
    /// The 7-day backfill and lease recovery are table scans; they used to run before every single
    /// job, so a burst of submissions meant a burst of full scans. They now run on a timer.
    /// </summary>
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(30);
    private DateTimeOffset _lastMaintenanceUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessNext(stoppingToken))
                    continue;

                var wake = wakeReader.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(wake, Task.Delay(TimeSpan.FromSeconds(2), stoppingToken));
                while (wakeReader.TryRead(out _)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SOC detection worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNext(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (DateTimeOffset.UtcNow - _lastMaintenanceUtc >= MaintenanceInterval)
        {
            _lastMaintenanceUtc = DateTimeOffset.UtcNow;
            await RunMaintenanceAsync(db, token);
        }

        var jobId = await ClaimNextJob(db, token);
        if (jobId is null)
            return false;

        return await ProcessJobAsync(scope, db, jobId.Value, token);
    }

    private static async Task RunMaintenanceAsync(AppDbContext db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "SocDetectionJobs" ("SubmissionId", "Status", "AttemptCount", "CreatedAtUtc")
            SELECT s."Id", 0, 0, NOW()
            FROM "Submissions" s
            WHERE s."Status" <> 'FlagSubmitted'
              AND s."SubmitTimeUtc" >= NOW() - INTERVAL '7 days'
              AND NOT EXISTS (
                  SELECT 1 FROM "SocDetectionJobs" j WHERE j."SubmissionId" = s."Id")
            ORDER BY s."Id"
            LIMIT 200
            ON CONFLICT ("SubmissionId") DO NOTHING
            """, token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SocDetectionJobs"
            SET "Status" = CASE WHEN "AttemptCount" >= 5 THEN 3 ELSE 0 END,
                "CompletedAtUtc" = CASE WHEN "AttemptCount" >= 5 THEN NOW() ELSE NULL END,
                "StartedAtUtc" = NULL,
                "LastError" = 'Detection lease expired before completion.'
            WHERE "Status" = 1 AND "StartedAtUtc" < {DateTimeOffset.UtcNow.AddMinutes(-5)}
            """, token);
    }

    private async Task<bool> ProcessJobAsync(AsyncServiceScope scope, AppDbContext db, long jobId,
        CancellationToken token)
    {
        var job = await db.SocDetectionJobs.SingleAsync(x => x.Id == jobId, token);
        try
        {
            await AnalyzeAsync(db, job.SubmissionId,
                scope.ServiceProvider.GetRequiredService<ISocEventPublisher>(),
                scope.ServiceProvider.GetRequiredService<SocRiskCaseService>(), token);
            job.Status = SocDetectionStatus.Completed;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.LastError = null;
            await db.SaveChangesAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Drop whatever the failed analysis left tracked (half-built evidence, outbox rows) so
            // recording the failure can't persist it, then reload the job cleanly.
            db.ChangeTracker.Clear();
            job = await db.SocDetectionJobs.SingleAsync(x => x.Id == jobId, token);
            job.Status = job.AttemptCount >= 5 ? SocDetectionStatus.Failed : SocDetectionStatus.Pending;
            job.CompletedAtUtc = job.Status == SocDetectionStatus.Failed ? DateTimeOffset.UtcNow : null;
            job.StartedAtUtc = null;
            job.LastError = Limit(ex.GetBaseException().Message, 1000);
            await db.SaveChangesAsync(token);
            logger.LogWarning(ex, "SOC analysis failed for submission {SubmissionId}.", job.SubmissionId);
        }
        return true;
    }

    private async Task AnalyzeAsync(AppDbContext db, int submissionId, ISocEventPublisher events,
        SocRiskCaseService cases, CancellationToken token)
    {
        var submission = await db.Submissions.IgnoreAutoIncludes().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == submissionId, token);
        if (submission is null)
            return;

        var fingerprint = FlagFingerprint.Compute(submission.Answer, _fingerprintKey);
        var eventId = SubmissionEventId(submission.Id);
        if (!await db.SocEvents.AsNoTracking().AnyAsync(x => x.EventId == eventId, token) &&
            !await db.SocEventOutbox.AsNoTracking().AnyAsync(x => x.EventId == eventId, token))
            events.Enqueue(new SocEventMessage(
                EventType: "FlagSubmission",
                Category: SocCategory.AntiCheat,
                Severity: submission.Status == AnswerResult.CheatDetected ? SocSeverity.Critical : SocSeverity.Info,
                Details: $"Submission {submission.Id} was classified as {submission.Status}.",
                Source: nameof(SocDetectionService),
                TeamId: submission.TeamId,
                UserId: submission.UserId,
                GameId: submission.GameId,
                ChallengeId: submission.ChallengeId,
                SubmissionId: submission.Id,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    submission.ParticipationId,
                    status = submission.Status.ToString(),
                    answerFingerprint = fingerprint
                }),
                OccurredAtUtc: submission.SubmitTimeUtc,
                EventId: eventId));

        if (submission.Status == AnswerResult.CheatDetected)
            await AnalyzeForeignFlag(db, submission, fingerprint, cases, token);
        else if (submission.Status == AnswerResult.WrongAnswer)
            await AnalyzeSharedWrongAnswer(db, submission, fingerprint, cases, token);
        else if (submission.Status == AnswerResult.Accepted)
        {
            await AnalyzeSynchronizedSolves(db, submission, cases, token);
            await AnalyzeFastSolve(db, submission, cases, token);
        }

        await AnalyzeBurst(db, submission, cases, token);
        await AnalyzeSharedNetwork(db, submission, cases, token);
    }

    private static async Task AnalyzeForeignFlag(AppDbContext db, Submission submission, string fingerprint,
        SocRiskCaseService cases, CancellationToken token)
    {
        var cheat = await db.CheatInfo.AsNoTracking()
            .Where(x => x.SubmissionId == submission.Id)
            .Select(x => new
            {
                SubmitParticipationId = x.SubmitTeamId,
                SourceParticipationId = x.SourceTeamId,
                SubmitTeamId = x.SubmitTeam.TeamId,
                SourceTeamId = x.SourceTeam.TeamId
            })
            .SingleOrDefaultAsync(token);
        if (cheat is null)
            return;

        var pairKey = SocRiskCaseService.PairKey(submission.GameId, cheat.SubmitTeamId, cheat.SourceTeamId);
        await cases.AddEvidenceAsync(new SocRiskSignal(
            pairKey, $"foreign-flag:{submission.Id}", "ForeignFlag",
            "Cross-Team Flag Submission",
            "A valid dynamic flag assigned to the other team was submitted.",
            70, 98, SocSeverity.Critical, submission.SubmitTimeUtc,
            submission.GameId, cheat.SubmitTeamId, cheat.SourceTeamId,
            submission.ChallengeId, submission.Id, fingerprint,
            new
            {
                submittingParticipationId = cheat.SubmitParticipationId,
                sourceParticipationId = cheat.SourceParticipationId,
                submission.Id
            }), token);

        var acceptedTeams = await db.Participations.AsNoTracking().CountAsync(x =>
            x.GameId == submission.GameId && x.Status == ParticipationStatus.Accepted, token);
        if (acceptedTeams == 2)
            await cases.AddEvidenceAsync(new SocRiskSignal(
                pairKey, $"head-to-head-transfer:{submission.Id}", "HeadToHeadTransfer",
                "Head-to-Head Flag Transfer",
                "A foreign team flag was used in a competition with exactly two accepted teams.",
                15, 99, SocSeverity.Critical, submission.SubmitTimeUtc,
                submission.GameId, cheat.SubmitTeamId, cheat.SourceTeamId,
                submission.ChallengeId, submission.Id, fingerprint,
                new { acceptedTeams }), token);

        var reciprocal = await db.CheatInfo.AsNoTracking().AnyAsync(x =>
            x.GameId == submission.GameId && x.SubmitTeamId == cheat.SourceParticipationId &&
            x.SourceTeamId == cheat.SubmitParticipationId, token);
        if (reciprocal)
            await cases.AddEvidenceAsync(new SocRiskSignal(
                pairKey,
                $"reciprocal-flags:{submission.GameId}:{Math.Min(cheat.SubmitTeamId, cheat.SourceTeamId)}:{Math.Max(cheat.SubmitTeamId, cheat.SourceTeamId)}",
                "ReciprocalFlagExchange", "Reciprocal Flag Exchange",
                "Both teams submitted flags assigned to the other, establishing a two-way correlation.",
                30, 100, SocSeverity.Critical, submission.SubmitTimeUtc,
                submission.GameId, cheat.SubmitTeamId, cheat.SourceTeamId,
                submission.ChallengeId, submission.Id, fingerprint), token);

        var circulation = await db.CheatInfo.AsNoTracking()
            .Where(x => x.GameId == submission.GameId && x.SourceTeamId == cheat.SourceParticipationId)
            .Select(x => x.SubmitTeamId).Distinct().CountAsync(token);
        if (circulation >= 3)
            await cases.AddEvidenceAsync(new SocRiskSignal(
                SocRiskCaseService.TeamKey(submission.GameId, cheat.SourceTeamId),
                $"flag-circulation:{submission.GameId}:{cheat.SourceParticipationId}:{submission.ChallengeId}",
                "MultiTeamFlagCirculation", "Multi-Team Flag Circulation",
                "One participation's flags were observed in submissions from three or more participations.",
                45, 95, SocSeverity.Critical, submission.SubmitTimeUtc,
                submission.GameId, cheat.SourceTeamId, null,
                submission.ChallengeId, submission.Id, fingerprint,
                new { sourceParticipationId = cheat.SourceParticipationId, distinctSubmittingParticipations = circulation }), token);
    }

    private static async Task AnalyzeSharedWrongAnswer(AppDbContext db, Submission submission, string fingerprint,
        SocRiskCaseService cases, CancellationToken token)
    {
        // Compare trimmed, case-folded answers so trivial edits ("Flag{x} ", "FLAG{X}") don't hide
        // a shared answer.
        var normalized = submission.Answer.Trim().ToLower();
        var matchingTeams = await db.Submissions.AsNoTracking()
            .Where(x => x.GameId == submission.GameId && x.ChallengeId == submission.ChallengeId &&
                x.Status == AnswerResult.WrongAnswer && x.Answer.Trim().ToLower() == normalized)
            .Select(x => x.TeamId).Distinct().Take(9).ToArrayAsync(token);
        if (matchingTeams.Length < 3)
            return;

        foreach (var otherTeamId in matchingTeams.Where(x => x != submission.TeamId).Take(8))
        {
            var pairKey = SocRiskCaseService.PairKey(submission.GameId, submission.TeamId, otherTeamId);
            await cases.AddEvidenceAsync(new SocRiskSignal(
                pairKey,
                $"shared-wrong:{submission.GameId}:{submission.ChallengeId}:{fingerprint}:{Math.Min(submission.TeamId, otherTeamId)}:{Math.Max(submission.TeamId, otherTeamId)}",
                "SharedWrongAnswer", "Shared Unusual Wrong Answer",
                "The same unusual wrong answer was submitted by at least three teams to one challenge.",
                15, 60, SocSeverity.Medium, submission.SubmitTimeUtc,
                submission.GameId, submission.TeamId, otherTeamId,
                submission.ChallengeId, submission.Id, fingerprint,
                new { distinctTeams = matchingTeams.Length }), token);
        }
    }

    /// <summary>Interactions that show a team actually started working on a challenge.</summary>
    private static readonly string[] InteractionEvents =
    [
        "ChallengeRuntimeProvisioned", "ChallengeAttachmentDownloaded", "PersonalizedArtifactDownloaded",
        "HintUnlocked"
    ];

    /// <summary>
    /// S2 — impossibly fast solve: the first correct flag lands within seconds of the team's
    /// first recorded interaction with the challenge (container started, file downloaded, hint,
    /// or an earlier attempt). Typical of a flag received from someone else. Bigger challenges get
    /// a longer allowance. Challenges with no recorded interaction are skipped (no baseline).
    /// </summary>
    private static async Task AnalyzeFastSolve(AppDbContext db, Submission submission,
        SocRiskCaseService cases, CancellationToken token)
    {
        var scored = await db.FirstSolves.AsNoTracking()
            .AnyAsync(x => x.SubmissionId == submission.Id, token);
        if (!scored)
            return;

        var points = await db.GameChallenges.AsNoTracking()
            .Where(c => c.Id == submission.ChallengeId)
            .Select(c => (int?)c.OriginalScore)
            .FirstOrDefaultAsync(token) ?? 100;

        DateTimeOffset? firstEvent = await db.SocEvents.AsNoTracking()
            .Where(e => e.GameId == submission.GameId && e.TeamId == submission.TeamId &&
                        e.ChallengeId == submission.ChallengeId && InteractionEvents.Contains(e.EventType) &&
                        e.TimestampUtc <= submission.SubmitTimeUtc)
            .MinAsync(e => (DateTimeOffset?)e.TimestampUtc, token);
        DateTimeOffset? firstAttempt = await db.Submissions.AsNoTracking()
            .Where(x => x.ParticipationId == submission.ParticipationId && x.ChallengeId == submission.ChallengeId &&
                        x.Id != submission.Id && x.SubmitTimeUtc <= submission.SubmitTimeUtc)
            .MinAsync(x => (DateTimeOffset?)x.SubmitTimeUtc, token);

        var started = new[] { firstEvent, firstAttempt }.Where(x => x is not null).Min();
        if (started is not { } begin)
            return;

        var seconds = (submission.SubmitTimeUtc - begin).TotalSeconds;
        var allowance = 20 + points / 50.0; // 100 pts → 22 s, 500 pts → 30 s, 1000 pts → 40 s
        if (seconds >= allowance)
            return;

        await cases.AddEvidenceAsync(new SocRiskSignal(
            SocRiskCaseService.TeamKey(submission.GameId, submission.TeamId),
            $"fast-solve:{submission.ParticipationId}:{submission.ChallengeId}",
            "ImpossiblyFastSolve", "Impossibly Fast Solve",
            $"Solved {seconds:F0} s after the team first touched the challenge (allowance {allowance:F0} s for {points} points).",
            15, 55, SocSeverity.Medium, submission.SubmitTimeUtc,
            submission.GameId, submission.TeamId, null,
            submission.ChallengeId, submission.Id,
            Metadata: new { seconds = Math.Round(seconds, 1), allowance, points }), token);
    }

    private static async Task AnalyzeBurst(AppDbContext db, Submission submission,
        SocRiskCaseService cases, CancellationToken token)
    {
        var burst = await db.Submissions.AsNoTracking().CountAsync(x =>
            x.ParticipationId == submission.ParticipationId &&
            x.SubmitTimeUtc >= submission.SubmitTimeUtc.AddMinutes(-1) &&
            x.SubmitTimeUtc <= submission.SubmitTimeUtc, token);
        if (burst < 12)
            return;

        await cases.AddEvidenceAsync(new SocRiskSignal(
            SocRiskCaseService.TeamKey(submission.GameId, submission.TeamId),
            $"submission-burst:{submission.ParticipationId}:{submission.SubmitTimeUtc:yyyyMMddHHmm}",
            "SubmissionBurst", "Suspicious Submission Burst",
            $"{burst} submissions were recorded within one minute for this participation.",
            10, 55, SocSeverity.Medium, submission.SubmitTimeUtc,
            submission.GameId, submission.TeamId, null,
            submission.ChallengeId, submission.Id,
            Metadata: new { burst, submission.ParticipationId }), token);
    }

    private static async Task AnalyzeSynchronizedSolves(AppDbContext db, Submission submission,
        SocRiskCaseService cases, CancellationToken token)
    {
        var currentSolve = await db.FirstSolves.AsNoTracking()
            .Where(x => x.SubmissionId == submission.Id)
            .Select(x => new { x.ParticipationId, x.Submission.SubmitTimeUtc })
            .SingleOrDefaultAsync(token);
        if (currentSolve is null)
            return;

        var start = currentSolve.SubmitTimeUtc.AddSeconds(-8);
        var end = currentSolve.SubmitTimeUtc.AddSeconds(8);
        var candidates = await db.FirstSolves.AsNoTracking()
            .Where(x => x.ChallengeId == submission.ChallengeId && x.ParticipationId != submission.ParticipationId &&
                x.Submission.SubmitTimeUtc >= start && x.Submission.SubmitTimeUtc <= end)
            .Select(x => new { x.ParticipationId, x.Participation.TeamId })
            .Take(20).ToArrayAsync(token);

        foreach (var candidate in candidates)
        {
            var rows = await db.FirstSolves.AsNoTracking()
                .Where(x => x.Challenge.GameId == submission.GameId &&
                    (x.ParticipationId == submission.ParticipationId ||
                     x.ParticipationId == candidate.ParticipationId))
                .Select(x => new { x.ChallengeId, x.ParticipationId, x.Submission.SubmitTimeUtc })
                .ToArrayAsync(token);
            var correlatedChallenges = rows.GroupBy(x => x.ChallengeId).Count(group =>
            {
                var ours = group.Where(x => x.ParticipationId == submission.ParticipationId).ToArray();
                var theirs = group.Where(x => x.ParticipationId == candidate.ParticipationId).ToArray();
                return ours.Any(a => theirs.Any(b => Math.Abs((a.SubmitTimeUtc - b.SubmitTimeUtc).TotalSeconds) <= 8));
            });
            if (correlatedChallenges < 3)
                continue;

            await cases.AddEvidenceAsync(new SocRiskSignal(
                SocRiskCaseService.PairKey(submission.GameId, submission.TeamId, candidate.TeamId),
                $"solve-sync:{submission.GameId}:{submission.ChallengeId}:{Math.Min(submission.TeamId, candidate.TeamId)}:{Math.Max(submission.TeamId, candidate.TeamId)}",
                "SynchronizedSolves", "Repeated Synchronized Solves",
                "The same two teams solved at least three challenges within eight seconds of each other.",
                20, 70, SocSeverity.High, submission.SubmitTimeUtc,
                submission.GameId, submission.TeamId, candidate.TeamId,
                submission.ChallengeId, submission.Id,
                Metadata: new { correlatedChallenges, windowSeconds = 8 }), token);
        }
    }

    private async Task AnalyzeSharedNetwork(AppDbContext db, Submission submission,
        SocRiskCaseService cases, CancellationToken token)
    {
        if (submission.UserId is null)
            return;
        var address = await db.Users.AsNoTracking().Where(x => x.Id == submission.UserId)
            .Select(x => x.IP).SingleOrDefaultAsync(token);
        if (address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            IPAddress.IsLoopback(address))
            return;

        var otherTeams = await db.UserParticipations.AsNoTracking()
            .Where(x => x.GameId == submission.GameId && x.TeamId != submission.TeamId && x.User.IP == address)
            .Select(x => x.TeamId).Distinct().Take(8).ToArrayAsync(token);
        if (otherTeams.Length == 0)
            return;
        var networkFingerprint = FlagFingerprint.Compute(address.ToString(), _fingerprintKey);
        foreach (var otherTeamId in otherTeams)
            await cases.AddEvidenceAsync(new SocRiskSignal(
                SocRiskCaseService.PairKey(submission.GameId, submission.TeamId, otherTeamId),
                $"shared-network:{submission.GameId}:{networkFingerprint}:{Math.Min(submission.TeamId, otherTeamId)}:{Math.Max(submission.TeamId, otherTeamId)}",
                "SharedNetwork", "Shared Network Signal",
                "Accounts in different teams were recently observed behind the same network address.",
                5, 25, SocSeverity.Low, submission.SubmitTimeUtc,
                submission.GameId, submission.TeamId, otherTeamId,
                submission.ChallengeId, submission.Id, networkFingerprint,
                new { networkFingerprint }), token);
    }

    private static async Task<long?> ClaimNextJob(AppDbContext db, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            UPDATE "SocDetectionJobs"
            SET "Status" = 1, "StartedAtUtc" = NOW(),
                "AttemptCount" = "AttemptCount" + 1, "LastError" = NULL
            WHERE "Id" = (
                SELECT "Id" FROM "SocDetectionJobs"
                WHERE "Status" = 0
                ORDER BY "CreatedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING "Id";
            """;
        var value = await command.ExecuteScalarAsync(token);
        return value is long id ? id : null;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private static Guid SubmissionEventId(int submissionId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"zarrar:soc:submission:{submissionId}"));
        return new Guid(digest.AsSpan(0, 16));
    }
}
