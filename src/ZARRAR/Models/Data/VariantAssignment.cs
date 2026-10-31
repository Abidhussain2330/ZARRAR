using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

/// <summary>
/// Immutable ownership record for a personalized static challenge artifact.
/// </summary>
/// <remarks>
/// <see cref="GameInstance"/> retains the runtime flag pointer used by older code. This entity is
/// the durable provenance record: it survives normal administration, records authenticated
/// downloads, and gives the SOC an exact artifact-hash-to-owner lookup.
/// </remarks>
[Index(nameof(AssignmentId), IsUnique = true)]
[Index(nameof(ChallengeId), nameof(ParticipationId), IsUnique = true)]
[Index(nameof(FlagContextId), IsUnique = true)]
[Index(nameof(ArtifactHash))]
public sealed class VariantAssignment
{
    [Key]
    public long Id { get; set; }

    public Guid AssignmentId { get; set; } = Guid.NewGuid();

    public int ChallengeId { get; set; }

    public int ParticipationId { get; set; }

    public int FlagContextId { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FirstDownloadedAtUtc { get; set; }

    public DateTimeOffset? LastDownloadedAtUtc { get; set; }

    public int DownloadCount { get; set; }

    [MaxLength(Limits.FileHashLength)]
    public string? ArtifactHash { get; set; }

    [Required]
    [MaxLength(32)]
    public string Mode { get; set; } = "Pool";

    [MaxLength(64)]
    public string? GeneratorVersion { get; set; }

    [MaxLength(128)]
    public string? SeedCommitment { get; set; }

    [MaxLength(8000)]
    public string? MetadataJson { get; set; }

    public DateTimeOffset? RetiredAtUtc { get; set; }

    public GameChallenge Challenge { get; set; } = null!;

    public Participation Participation { get; set; } = null!;

    public FlagContext FlagContext { get; set; } = null!;

    public VariantBuildJob? BuildJob { get; set; }

    public void RegisterDownload(DateTimeOffset timestamp)
    {
        if (timestamp < AssignedAtUtc)
            throw new ArgumentOutOfRangeException(nameof(timestamp),
                "A download cannot predate the assignment.");

        FirstDownloadedAtUtc ??= timestamp;
        LastDownloadedAtUtc = timestamp;
        checked
        {
            DownloadCount++;
        }
    }
}
