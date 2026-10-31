using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

[JsonConverter(typeof(JsonStringEnumConverter<VariantBuildStatus>))]
public enum VariantBuildStatus : byte { Pending, Running, Succeeded, Failed, Quarantined }

[Index(nameof(TemplateId), nameof(ParticipationId), IsUnique = true)]
[Index(nameof(Status), nameof(CreatedAtUtc))]
public sealed class VariantBuildJob
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public long TemplateId { get; set; }
    public int ParticipationId { get; set; }
    public Guid FlagInstanceId { get; set; } = Guid.NewGuid();
    public long? VariantAssignmentId { get; set; }
    [Required, MaxLength(64)] public string SeedCommitment { get; set; } = string.Empty;
    public VariantBuildStatus Status { get; set; } = VariantBuildStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    [MaxLength(64)] public string? ArtifactHash { get; set; }
    [MaxLength(64)] public string? GeneratorVersion { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public VariantTemplate Template { get; set; } = null!;
    public Participation Participation { get; set; } = null!;
    public VariantAssignment? VariantAssignment { get; set; }
}
