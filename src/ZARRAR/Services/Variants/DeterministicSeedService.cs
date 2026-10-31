using System.Security.Cryptography;
using System.Text;
using ZARRAR.Models.Internal;
using Microsoft.Extensions.Options;

namespace ZARRAR.Services.Variants;

/// <summary>Domain-separated deterministic seeds; raw seeds are never persisted.</summary>
public sealed class DeterministicSeedService(IOptions<VariantBuilderOptions> options)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.SeedKey);

    public bool IsConfigured => _key.Length >= 32;

    public byte[] Derive(long templateId, string version, int challengeId, int participationId)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Variant builder seed key must contain at least 32 UTF-8 bytes.");
        return HMACSHA256.HashData(_key,
            Encoding.UTF8.GetBytes($"zarrar:variant:v1:{templateId}:{version}:{challengeId}:{participationId}"));
    }

    public string Commitment(ReadOnlySpan<byte> seed) =>
        Convert.ToHexStringLower(SHA256.HashData(seed));

    public string Rank(int challengeId, int participationId, string datasetKey)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Variant builder seed key must contain at least 32 UTF-8 bytes.");
        var digest = HMACSHA256.HashData(_key,
            Encoding.UTF8.GetBytes($"zarrar:dataset:v1:{challengeId}:{participationId}:{datasetKey}"));
        return Convert.ToHexStringLower(digest);
    }
}
