using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ZARRAR.Models.Request.Admin;

public record SocMetricPoint(DateTimeOffset Time, double CpuPercent, double MemoryPercent,
    double StoragePercent, int RunningInstances);
public record SocHostSnapshot(string Node, string Status, DateTimeOffset SampledAt, int CpuCores,
    double? CpuPercent, double? LoadAverage, long? MemoryUsedBytes, long? MemoryTotalBytes,
    double? MemoryPercent, long? StorageUsedBytes, long? StorageTotalBytes, double? StoragePercent,
    IReadOnlyDictionary<string, string> Services);
public record SocContainerMetric(Guid ContainerGuid, double? CpuPercent, long? MemoryUsedBytes,
    long? MemoryLimitBytes, long NetworkRxBytes, long NetworkTxBytes, int RestartCount, string RuntimeStatus);
public record SocResourceSnapshot(SocHostSnapshot Host, SocMetricPoint Point,
    SocContainerMetric[] ContainerMetrics);
public record SocProviderSnapshot(string Provider, string Node, string Status, int? Capacity,
    int Running, int Pending, int Unavailable, string Isolation, string? Version, DateTimeOffset SampledAt);
public record SocOverviewModel(int ActiveTeams, int OnlineUsers, int ActiveInstances, int TotalSubmissions,
    int CorrectSolves, int FailedAttempts, int OpenAlerts, int CriticalAlerts, int OpenRiskCases, int FailedInstances,
    SocProviderSnapshot Provider, SocHostSnapshot Host, SocMetricPoint[] History,
    SocContainerMetric[] ContainerMetrics);
public record SocInstanceModel(Guid InstanceId, string ContainerId, int TeamId, string Team, int GameId,
    string Game, int ChallengeId, string Challenge, string Node, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string Status, string NetworkMode, double? CpuPercent,
    long? MemoryUsedBytes, long? MemoryLimitBytes, long NetworkRxBytes, long NetworkTxBytes,
    int RestartCount, int? CpuLimitTenths, int? ConfiguredMemoryMb);
public record SocAlertModel(long Id, DateTimeOffset CreatedAt, string Severity, string Category, string Event,
    string Description, int? TeamId, string? Team, int? RelatedTeamId, string? RelatedTeam,
    int? GameId, string? Game, int? ChallengeId, string? Challenge, Guid? InstanceId,
    int RiskImpact, string Status, string? EvidenceFingerprint, string? Evidence, string? Notes);
public record SocRiskCaseModel(Guid Id, string CaseKey, string Title, string Summary, string Status,
    int RiskScore, int Confidence, int RuleHitCount, int EvidenceCount, DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt, DateTimeOffset UpdatedAt, int GameId, string Game, int PrimaryTeamId,
    string PrimaryTeam, int? RelatedTeamId, string? RelatedTeam, Guid? AssignedAdminId, string? Notes);
public record SocRiskEvidenceModel(long Id, string EvidenceKey, string Kind, int Weight, int Confidence,
    DateTimeOffset OccurredAt, int? TeamId, int? RelatedTeamId, int? ChallengeId, int? SubmissionId,
    long? AlertId, string? Fingerprint, string? MetadataJson);
public record SocRiskCaseDetailModel(SocRiskCaseModel Case, SocRiskEvidenceModel[] Evidence);
public record SocRiskCaseUpdateModel(string Status, [param: MaxLength(2000)] string? Notes,
    Guid? AssignedAdminId);
public record SocTeamRiskModel(int TeamId, string Team, int RiskScore, string Classification,
    int OpenAlerts, int CriticalAlerts, int Submissions, int Solves, int RunningInstances,
    string[] Reasons);
public record SocTeamInstanceLimitModel(int ParticipationId, int GameId, string Game,
    int RunningInstances, int DefaultLimit, int? OverrideLimit, int EffectiveLimit);
public record SocTeamInstanceLimitUpdateModel([param: Range(0, 1000)] int? Limit);
public record SocFlagModel(string Fingerprint, int TeamId, string Team, int GameId, string Game,
    int ChallengeId, string Challenge, Guid? InstanceId, DateTimeOffset? GeneratedAt, string Status,
    Guid? AssignmentId, string? ArtifactHash, DateTimeOffset? AssignedAt,
    DateTimeOffset? FirstDownloadedAt, DateTimeOffset? LastDownloadedAt, int DownloadCount,
    bool Immutable);
public record SocTimelineModel(long Id, DateTimeOffset Timestamp, string Severity, string Category,
    string Event, int? TeamId, string? Team, int? GameId, string? Game, int? ChallengeId,
    string? Challenge, Guid? InstanceId, string Details, Guid? EventId, string? Source,
    string? CorrelationId, int? RelatedTeamId, string? RelatedTeam, Guid? ActorUserId,
    Guid? UserId);
public record SocTimelineDetailModel(long Id, Guid? EventId, DateTimeOffset Timestamp, string Severity,
    string Category, string Event, string? Source, string Details, string? CorrelationId,
    string? PayloadJson, int? TeamId, string? Team, int? RelatedTeamId, string? RelatedTeam,
    Guid? ActorUserId, Guid? UserId, int? GameId, string? Game, int? ChallengeId,
    string? Challenge, Guid? InstanceId, int? SubmissionId);
public record SocOutboxStatusModel(int Pending, int Retrying, int DeadLettered,
    DateTimeOffset? OldestPending, DateTimeOffset? LastProcessed);
public record SocDetectionQueueStatusModel(int Pending, int Running, int Failed,
    DateTimeOffset? OldestPending, DateTimeOffset? LastCompleted);
public record SocDetectionJobModel(long Id, int SubmissionId, string Status, int AttemptCount,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? LastError);
public record SocOutboxEntryModel(long Id, Guid EventId, DateTimeOffset OccurredAt, string State,
    int AttemptCount, DateTimeOffset? NextAttemptAt, string? LastError, string EventType,
    string Source, string Severity, string Category, int? TeamId, int? RelatedTeamId,
    Guid? ActorUserId, Guid? UserId, int? GameId, int? ChallengeId, Guid? InstanceId,
    int? SubmissionId, string? CorrelationId);
public record SocUpdateDataModel(Guid EventId, string EventType, string Severity, string Category,
    string Source, Guid? ActorUserId, Guid? UserId, int? TeamId, int? GameId, int? ChallengeId,
    Guid? InstanceId);
public record SocAlertUpdateDataModel(long Id, string Event, string Severity, string Category,
    int? TeamId, int? GameId, int? ChallengeId, int RiskImpact);
public record SocUpdateModel(string Kind, DateTimeOffset Timestamp, JsonElement Data);
public record SocDetectionEvent(int SubmissionId);
public record SocAlertUpdateModel(string Status, string? Notes, Guid? AssignedAdminId);

// ---- S1: respond from a case -----------------------------------------------------------------

/// <summary>A case's actionable targets: its teams (with members) and their recent submissions.</summary>
public record SocCaseTargetsModel(SocCaseTeamTarget[] Teams, SocCaseSubmissionTarget[] Submissions);
public record SocCaseTeamTarget(int TeamId, string Team, int ParticipationId, string Status,
    SocCaseMemberTarget[] Members);
public record SocCaseMemberTarget(Guid UserId, string UserName, string Role);
public record SocCaseSubmissionTarget(int Id, int TeamId, string? Team, string? UserName, string? Challenge,
    DateTimeOffset SubmittedAt, string Status, bool Scored);

/// <summary>
/// One response action on a case: suspend-team, reinstate-team, ban-user, sign-out-user,
/// void-submission or note. The note is required for every action except reinstate, so the case
/// record always says why.
/// </summary>
public record SocCaseActionModel(
    [param: Required, MaxLength(32)] string Action,
    int? TeamId,
    Guid? UserId,
    int? SubmissionId,
    [param: MaxLength(500)] string? Note);

public record SocAlertBulkUpdateModel([param: Required] long[] Ids, [param: Required] string Status);

// ---- S3: ledger ------------------------------------------------------------------------------

/// <summary>Result of re-checking the SOC timeline's hash-chained seals.</summary>
public record SocLedgerStatusModel(bool Intact, int VerifiedSeals, int VerifiedEvents, int UnsealedEvents,
    long? BrokenAtSealId, string? Problem, DateTimeOffset? LastSealAt, int PrunedSeals);
