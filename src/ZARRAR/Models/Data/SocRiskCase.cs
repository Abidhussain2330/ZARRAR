using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

[Index(nameof(CaseKey), IsUnique = true)]
[Index(nameof(Status), nameof(RiskScore), nameof(UpdatedAtUtc))]
[Index(nameof(GameId), nameof(PrimaryTeamId), nameof(RelatedTeamId))]
public sealed class SocRiskCase
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    [Required, MaxLength(160)] public string CaseKey { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Summary { get; set; } = string.Empty;
    public SocAlertStatus Status { get; set; } = SocAlertStatus.Open;
    [Range(0, 100)] public int RiskScore { get; set; }
    [Range(0, 100)] public int Confidence { get; set; }
    public int RuleHitCount { get; set; }
    public int EvidenceCount { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int GameId { get; set; }
    public int PrimaryTeamId { get; set; }
    public int? RelatedTeamId { get; set; }
    public Guid? AssignedAdminId { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public Game Game { get; set; } = null!;
    public Team PrimaryTeam { get; set; } = null!;
    public Team? RelatedTeam { get; set; }
    public UserInfo? AssignedAdmin { get; set; }
    public List<SocRiskEvidence> Evidence { get; set; } = [];
}
