namespace ZARRAR.Services.Soc;

/// <summary>
/// Typed, secret-safe contract used by platform modules to publish security and audit events.
/// PayloadJson must never contain raw flags, passwords, tokens, private keys, or other secrets.
/// </summary>
public sealed record SocEventMessage(
    string EventType,
    SocCategory Category,
    SocSeverity Severity,
    string Details,
    string Source = "ZARRAR",
    int? TeamId = null,
    int? RelatedTeamId = null,
    Guid? UserId = null,
    Guid? ActorUserId = null,
    int? GameId = null,
    int? ChallengeId = null,
    Guid? InstanceId = null,
    int? SubmissionId = null,
    string? CorrelationId = null,
    string? PayloadJson = null,
    DateTimeOffset? OccurredAtUtc = null,
    Guid? EventId = null);
