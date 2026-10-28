using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

public enum SocDetectionStatus : byte { Pending, Running, Completed, Failed }

[Index(nameof(SubmissionId), IsUnique = true)]
[Index(nameof(Status), nameof(CreatedAtUtc))]
public sealed class SocDetectionJob
{
    [Key] public long Id { get; set; }
    public int SubmissionId { get; set; }
    public SocDetectionStatus Status { get; set; } = SocDetectionStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public Submission Submission { get; set; } = null!;
}
