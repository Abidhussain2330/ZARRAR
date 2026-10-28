using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

[Index(nameof(FlagContextId), IsUnique = true)]
[Index(nameof(ChallengeId), nameof(DatasetKey), IsUnique = true)]
[Index(nameof(ChallengeId), nameof(IsEnabled), nameof(SelectedAtUtc))]
[Index(nameof(ChallengeId), nameof(SelectedParticipationId), IsUnique = true)]
public sealed class VariantDatasetItem
{
    [Key] public long Id { get; set; }
    public int ChallengeId { get; set; }
    public int FlagContextId { get; set; }
    public long? TemplateId { get; set; }
    [Required, MaxLength(128)] public string DatasetKey { get; set; } = string.Empty;
    [MaxLength(64)] public string? ArtifactHash { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SelectedAtUtc { get; set; }
    public int? SelectedParticipationId { get; set; }
    public GameChallenge Challenge { get; set; } = null!;
    public FlagContext FlagContext { get; set; } = null!;
    public VariantTemplate? Template { get; set; }
    public Participation? SelectedParticipation { get; set; }
}
