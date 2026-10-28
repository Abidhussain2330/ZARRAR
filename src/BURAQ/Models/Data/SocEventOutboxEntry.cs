using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

/// <summary>
/// Durable transactional hand-off between platform mutations and the SOC timeline.
/// Producers insert this row using their existing <see cref="AppDbContext"/> transaction;
/// <c>SocEventOutboxDispatcher</c> later materializes it as a <see cref="SocEvent"/>.
/// </summary>
[Table("SocEventOutbox")]
[Index(nameof(EventId), IsUnique = true)]
[Index(nameof(ProcessedAtUtc), nameof(NextAttemptAtUtc), nameof(Id))]
[Index(nameof(GameId), nameof(OccurredAtUtc))]
public class SocEventOutboxEntry
{
    [Key]
    public long Id { get; set; }

    public Guid EventId { get; set; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public SocSeverity Severity { get; set; }

    public SocCategory Category { get; set; }

    [MaxLength(80)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Source { get; set; } = "BURAQ";

    [MaxLength(1000)]
    public string Details { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    [MaxLength(8000)]
    public string? PayloadJson { get; set; }

    public int? TeamId { get; set; }

    public int? RelatedTeamId { get; set; }

    /// <summary>The authenticated user who performed the action, when available.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The user account affected by the event, when applicable.</summary>
    public Guid? UserId { get; set; }

    public int? GameId { get; set; }

    public int? ChallengeId { get; set; }

    public Guid? InstanceId { get; set; }

    public int? SubmissionId { get; set; }
}
