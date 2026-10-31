using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

[Index(nameof(EvidenceKey), IsUnique = true)]
[Index(nameof(RiskCaseId), nameof(OccurredAtUtc))]
[Index(nameof(SubmissionId))]
public sealed class SocRiskEvidence
{
    [Key] public long Id { get; set; }
    public Guid RiskCaseId { get; set; }
    [Required, MaxLength(200)] public string EvidenceKey { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string Kind { get; set; } = string.Empty;
    [Range(0, 100)] public int Weight { get; set; }
    [Range(0, 100)] public int Confidence { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public int? TeamId { get; set; }
    public int? RelatedTeamId { get; set; }
    public int? ChallengeId { get; set; }
    public int? SubmissionId { get; set; }
    public long? AlertId { get; set; }
    [MaxLength(128)] public string? Fingerprint { get; set; }
    [MaxLength(4000)] public string? MetadataJson { get; set; }
    public SocRiskCase RiskCase { get; set; } = null!;
    public Submission? Submission { get; set; }
    public SocAlert? Alert { get; set; }
}
