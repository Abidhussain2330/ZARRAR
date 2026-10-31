using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Internal;

public sealed class VariantBuilderOptions
{
    /// <summary>HMAC root for deterministic seed derivation. Supplied by deployment secret storage.</summary>
    public string SeedKey { get; set; } = string.Empty;

    /// <summary>HTTPS origin of the isolated, separately operated builder control plane.</summary>
    public string ControlPlaneOrigin { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    [Range(1024, 536870912)]
    public int MaxArtifactBytes { get; set; } = 64 * 1024 * 1024;

    [Range(1, 16)]
    public int Parallelism { get; set; } = 2;

    public bool Enabled { get; set; }
}
