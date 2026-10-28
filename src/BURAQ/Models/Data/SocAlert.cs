using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

public enum SocSeverity : byte { Info, Low, Medium, High, Critical }
public enum SocCategory : byte { Infrastructure, AntiCheat, Authentication, PlatformSecurity }
public enum SocAlertStatus : byte { Open, Investigating, Resolved, Clean, Escalated }

[Index(nameof(CreatedAtUtc))]
[Index(nameof(TeamId), nameof(Status), nameof(CreatedAtUtc))]
[Index(nameof(Category), nameof(Severity), nameof(Status))]
[Index(nameof(SourceKey), IsUnique = true)]
public class SocAlert
{
    [Key] public long Id { get; set; }
    [MaxLength(160)] public string SourceKey { get; set; } = string.Empty;
    [MaxLength(120)] public string Event { get; set; } = string.Empty;
    [MaxLength(1000)] public string Description { get; set; } = string.Empty;
    public SocSeverity Severity { get; set; }
    public SocCategory Category { get; set; }
    public SocAlertStatus Status { get; set; } = SocAlertStatus.Open;
    public int RiskImpact { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? TeamId { get; set; }
    public int? RelatedTeamId { get; set; }
    public int? GameId { get; set; }
    public int? ChallengeId { get; set; }
    public Guid? InstanceId { get; set; }
    public int? SubmissionId { get; set; }
    [MaxLength(128)] public string? EvidenceFingerprint { get; set; }
    [MaxLength(4000)] public string? Evidence { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }
    public Guid? AssignedAdminId { get; set; }
}
