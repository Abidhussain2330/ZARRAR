using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Soc;

public sealed record SocRiskSignal(
    string CaseKey,
    string EvidenceKey,
    string Kind,
    string Title,
    string Summary,
    int Weight,
    int Confidence,
    SocSeverity Severity,
    DateTimeOffset OccurredAtUtc,
    int GameId,
    int PrimaryTeamId,
    int? RelatedTeamId = null,
    int? ChallengeId = null,
    int? SubmissionId = null,
    string? Fingerprint = null,
    object? Metadata = null);

public static class SocRiskScoring
{
    /// <summary>
    /// Evidence loses half its weight every half-life, so a case driven by old signals cools down
    /// instead of staying "hot" forever. Confidence does not decay: what was proven stays proven.
    /// Configure with <c>Soc:RiskHalfLifeHours</c> (default 6 h; 0 disables decay).
    /// </summary>
    public static TimeSpan HalfLife { get; private set; } = TimeSpan.FromHours(6);

    /// <summary>Resolved or clean cases reopen only when new evidence pushes the current risk this high,
    /// or the new signal itself is High/Critical. Keeps low-grade noise from undoing triage.</summary>
    public const int ReopenRisk = 50;

    /// <summary>Evidence at or above this confidence is proof (a foreign dynamic flag, a two-way
    /// exchange) and never fades: proven cheating doesn't become less true with time.</summary>
    public const int ProofConfidence = 95;

    public static void Configure(IConfiguration configuration)
    {
        var hours = configuration.GetValue<double?>("Soc:RiskHalfLifeHours") ?? 6;
        HalfLife = hours <= 0 ? TimeSpan.Zero : TimeSpan.FromHours(Math.Clamp(hours, 0.25, 24 * 30));
    }

    public static (int Risk, int Confidence, int RuleHits) Calculate(
        IEnumerable<(int Weight, int Confidence, string Kind)> evidence)
    {
        var rows = evidence.ToArray();
        if (rows.Length == 0)
            return (0, 0, 0);
        var kinds = rows.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Count();
        var risk = Math.Min(100, rows.Sum(x => Math.Clamp(x.Weight, 0, 100)));
        var confidence = Math.Min(100,
            rows.Max(x => Math.Clamp(x.Confidence, 0, 100)) + Math.Min(15, (kinds - 1) * 5));
        return (risk, confidence, kinds);
    }

    /// <summary>Risk as of <paramref name="now" />, with each piece of evidence decayed by its age.</summary>
    public static (int Risk, int Confidence, int RuleHits) CalculateAt(
        IEnumerable<(int Weight, int Confidence, string Kind, DateTimeOffset OccurredAt)> evidence,
        DateTimeOffset now)
    {
        var rows = evidence.ToArray();
        if (rows.Length == 0)
            return (0, 0, 0);

        var basis = Calculate(rows.Select(x => (x.Weight, x.Confidence, x.Kind)));
        if (HalfLife <= TimeSpan.Zero)
            return basis;

        var decayed = rows.Sum(x =>
        {
            var weight = Math.Clamp(x.Weight, 0, 100);
            if (x.Confidence >= ProofConfidence)
                return (double)weight;

            var age = Math.Max(0, (now - x.OccurredAt).TotalHours);
            return weight * Math.Pow(0.5, age / HalfLife.TotalHours);
        });

        return ((int)Math.Round(Math.Min(100, decayed)), basis.Confidence, basis.RuleHits);
    }
}

/// <summary>Creates deduplicated evidence and maintains one durable investigation case per subject/pair.</summary>
public sealed class SocRiskCaseService(
    AppDbContext db,
    ISocAlertService alerts,
    ISocEventPublisher events)
{
    private const int AdvisoryNamespace = 0x534F4352; // SOCR

    public static string PairKey(int gameId, int firstTeamId, int secondTeamId)
    {
        var low = Math.Min(firstTeamId, secondTeamId);
        var high = Math.Max(firstTeamId, secondTeamId);
        return $"pair:{gameId}:{low}:{high}";
    }

    public static string TeamKey(int gameId, int teamId) => $"team:{gameId}:{teamId}";

    public async Task<SocRiskCase> AddEvidenceAsync(SocRiskSignal signal, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.CaseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.EvidenceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signal.Kind);
        if (signal.PrimaryTeamId <= 0 || signal.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(signal), "Risk signals require server-derived game/team IDs.");

        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
            [AdvisoryNamespace, LockKey(signal.CaseKey)], cancellationToken: token);

        var riskCase = await db.SocRiskCases.Include(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.CaseKey == signal.CaseKey, token);
        if (riskCase is null)
        {
            riskCase = new SocRiskCase
            {
                CaseKey = Limit(signal.CaseKey, 160),
                Title = Limit(signal.Title, 120),
                Summary = Limit(signal.Summary, 1000),
                GameId = signal.GameId,
                PrimaryTeamId = signal.PrimaryTeamId,
                RelatedTeamId = signal.RelatedTeamId,
                FirstSeenAtUtc = signal.OccurredAtUtc,
                LastSeenAtUtc = signal.OccurredAtUtc
            };
            db.SocRiskCases.Add(riskCase);
        }

        var existing = riskCase.Evidence.FirstOrDefault(x => x.EvidenceKey == signal.EvidenceKey) ??
            await db.SocRiskEvidence.SingleOrDefaultAsync(x => x.EvidenceKey == signal.EvidenceKey, token);
        if (existing is not null)
        {
            await transaction.RollbackAsync(token);
            return riskCase;
        }

        // Alert fatigue: one alert per kind per case per half hour pages the analyst; further hits
        // still add evidence and risk, just without a new alert. Proof-grade signals always alert.
        var recentAlertOfKind = riskCase.Evidence.Any(x =>
            x.Kind == signal.Kind && x.AlertId != null &&
            x.OccurredAtUtc >= signal.OccurredAtUtc.AddMinutes(-30));
        var alert = recentAlertOfKind && signal.Confidence < SocRiskScoring.ProofConfidence
            ? null
            : await alerts.CreateAsync(new SocAlert
        {
            SourceKey = AlertKey(signal.EvidenceKey),
            Event = Limit(signal.Title, 120),
            Description = Limit(signal.Summary, 1000),
            Severity = signal.Severity,
            Category = SocCategory.AntiCheat,
            RiskImpact = Math.Clamp(signal.Weight, 0, 100),
            TeamId = signal.PrimaryTeamId,
            RelatedTeamId = signal.RelatedTeamId,
            GameId = signal.GameId,
            ChallengeId = signal.ChallengeId,
            SubmissionId = signal.SubmissionId,
            EvidenceFingerprint = LimitNullable(signal.Fingerprint, 128),
            Evidence = signal.Metadata is null ? null : Limit(JsonSerializer.Serialize(signal.Metadata), 4000)
        }, token);

        var evidence = new SocRiskEvidence
        {
            RiskCase = riskCase,
            EvidenceKey = Limit(signal.EvidenceKey, 200),
            Kind = Limit(signal.Kind, 64),
            Weight = Math.Clamp(signal.Weight, 0, 100),
            Confidence = Math.Clamp(signal.Confidence, 0, 100),
            OccurredAtUtc = signal.OccurredAtUtc,
            TeamId = signal.PrimaryTeamId,
            RelatedTeamId = signal.RelatedTeamId,
            ChallengeId = signal.ChallengeId,
            SubmissionId = signal.SubmissionId,
            Alert = alert,
            Fingerprint = LimitNullable(signal.Fingerprint, 128),
            MetadataJson = signal.Metadata is null ? null : Limit(JsonSerializer.Serialize(signal.Metadata), 4000)
        };
        riskCase.Evidence.Add(evidence);
        var score = SocRiskScoring.CalculateAt(riskCase.Evidence.Select(x =>
            (x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc)), DateTimeOffset.UtcNow);
        // A triaged case reopens only for a serious signal; minor repeats are recorded but don't
        // undo the analyst's decision.
        var reopened = riskCase.Status is SocAlertStatus.Resolved or SocAlertStatus.Clean &&
                       (signal.Severity >= SocSeverity.High || score.Risk >= SocRiskScoring.ReopenRisk);
        if (reopened)
            riskCase.Status = SocAlertStatus.Open;
        riskCase.Title = Limit(signal.Title, 120);
        riskCase.Summary = Limit(signal.Summary, 1000);
        riskCase.RiskScore = score.Risk;
        riskCase.Confidence = score.Confidence;
        riskCase.RuleHitCount = score.RuleHits;
        riskCase.EvidenceCount = riskCase.Evidence.Count;
        riskCase.FirstSeenAtUtc = riskCase.FirstSeenAtUtc == default || signal.OccurredAtUtc < riskCase.FirstSeenAtUtc
            ? signal.OccurredAtUtc : riskCase.FirstSeenAtUtc;
        riskCase.LastSeenAtUtc = signal.OccurredAtUtc > riskCase.LastSeenAtUtc
            ? signal.OccurredAtUtc : riskCase.LastSeenAtUtc;
        riskCase.UpdatedAtUtc = DateTimeOffset.UtcNow;

        events.Enqueue(new SocEventMessage(
            EventType: reopened ? "SocRiskCaseReopened" : "SocRiskCaseUpdated",
            Category: SocCategory.AntiCheat,
            Severity: signal.Severity,
            Details: $"Risk case {riskCase.Id} received {signal.Kind} evidence.",
            Source: nameof(SocRiskCaseService),
            TeamId: riskCase.PrimaryTeamId,
            RelatedTeamId: riskCase.RelatedTeamId,
            GameId: riskCase.GameId,
            ChallengeId: signal.ChallengeId,
            SubmissionId: signal.SubmissionId,
            CorrelationId: riskCase.Id.ToString(),
            PayloadJson: JsonSerializer.Serialize(new
            {
                riskCase.Id,
                evidence.EvidenceKey,
                evidence.Kind,
                riskCase.RiskScore,
                riskCase.Confidence,
                riskCase.EvidenceCount
            })));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return riskCase;
    }

    public static void Recalculate(SocRiskCase riskCase)
    {
        var score = SocRiskScoring.CalculateAt(riskCase.Evidence.Select(x =>
            (x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc)), DateTimeOffset.UtcNow);
        riskCase.RiskScore = score.Risk;
        riskCase.Confidence = score.Confidence;
        riskCase.RuleHitCount = score.RuleHits;
        riskCase.EvidenceCount = riskCase.Evidence.Count;
        riskCase.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static int LockKey(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BinaryPrimitives.ReadInt32LittleEndian(digest);
    }

    private static string AlertKey(string evidenceKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(evidenceKey));
        return $"risk:{Convert.ToHexStringLower(digest)}";
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static string? LimitNullable(string? value, int length) =>
        value is null ? null : Limit(value, length);
}
