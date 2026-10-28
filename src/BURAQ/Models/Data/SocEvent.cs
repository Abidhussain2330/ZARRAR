using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

[Index(nameof(EventId), IsUnique = true)]
[Index(nameof(TimestampUtc))]
[Index(nameof(TeamId), nameof(TimestampUtc))]
[Index(nameof(GameId), nameof(TimestampUtc))]
[Index(nameof(Category), nameof(EventType), nameof(TimestampUtc))]
public class SocEvent
{
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// Stable idempotency key copied from the transactional outbox. Legacy events may be null.
    /// </summary>
    public Guid? EventId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public SocSeverity Severity { get; set; }

    public SocCategory Category { get; set; }

    [MaxLength(80)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Source { get; set; }

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
