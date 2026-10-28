using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

[JsonConverter(typeof(JsonStringEnumConverter<VariantTemplateKind>))]
public enum VariantTemplateKind : byte { Reverse, Pwn, Crypto, Stego }

[JsonConverter(typeof(JsonStringEnumConverter<VariantTemplateStatus>))]
public enum VariantTemplateStatus : byte { Draft, Validated, Disabled }

[Index(nameof(ChallengeId), nameof(Version), IsUnique = true)]
[Index(nameof(ManifestHash))]
public sealed class VariantTemplate
{
    [Key] public long Id { get; set; }
    public int ChallengeId { get; set; }
    [Required, MaxLength(80)] public string Version { get; set; } = string.Empty;
    public VariantTemplateKind Kind { get; set; }
    [Required, MaxLength(16000)] public string ManifestJson { get; set; } = "{}";
    [Required, MaxLength(64)] public string ManifestHash { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string BuilderImageDigest { get; set; } = string.Empty;
    [Required, MaxLength(256)] public string OutputFileName { get; set; } = "challenge.bin";
    public VariantTemplateStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidatedAtUtc { get; set; }
    public GameChallenge Challenge { get; set; } = null!;
    public List<VariantBuildJob> Jobs { get; set; } = [];
    public List<VariantDatasetItem> DatasetItems { get; set; } = [];
}
