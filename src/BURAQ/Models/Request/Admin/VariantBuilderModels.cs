using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Admin;

public sealed class VariantTemplateCreateModel
{
    [Required, MaxLength(80)] public string Version { get; set; } = string.Empty;
    public VariantTemplateKind Kind { get; set; }
    [Required, MaxLength(16000)] public string ManifestJson { get; set; } = "{}";
    [Required, MaxLength(256)] public string BuilderImageDigest { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string OutputFileName { get; set; } = "challenge.bin";
}

public sealed record VariantTemplateModel(
    long Id, int ChallengeId, string Challenge, string Version, VariantTemplateKind Kind,
    string ManifestHash, string BuilderImageDigest, string OutputFileName,
    VariantTemplateStatus Status, int Jobs, int Succeeded, int Failed, int DatasetItems,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? ValidatedAtUtc);

public sealed record VariantBuildJobModel(
    Guid Id, long TemplateId, int ParticipationId, int TeamId, string Team,
    VariantBuildStatus Status, int AttemptCount, string SeedCommitment,
    string? ArtifactHash, string? GeneratorVersion, string? LastError,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record VariantBuilderStatusModel(
    bool Enabled, bool Configured, int ValidatedTemplates, int DatasetAvailable,
    int Pending, int Running, int Succeeded, int Failed, DateTimeOffset? OldestPendingAtUtc);
