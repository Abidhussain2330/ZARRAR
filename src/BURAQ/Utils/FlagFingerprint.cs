using System.Security.Cryptography;
using System.Text;

namespace BURAQ.Utils;

/// <summary>
/// Produces non-reversible, correlatable references to flag material.
/// </summary>
/// <remarks>
/// Flags are competition secrets. Writing them verbatim into logs, the database log sink, the
/// SignalR administrator stream or traffic-capture metadata puts them in reach of operators,
/// observability pipelines, backups and anyone who compromises those systems — a single leak
/// invalidates a challenge for every team (findings F-004 and F-021).
///
/// A keyed HMAC preserves the only property operators actually need — telling whether two
/// submissions carried the same answer, and whether a submission matched a known flag — while
/// making the value useless to anyone who obtains it. It is keyed rather than a bare hash so that
/// a low-entropy flag (a short or templated string) cannot be recovered by brute force.
/// </remarks>
public static class FlagFingerprint
{
    /// <summary>
    /// Marker prefix, so a fingerprint is never mistaken for a real flag in an operator's log.
    /// </summary>
    public const string Prefix = "fp:";

    /// <summary>
    /// Placeholder used when there is no value to fingerprint.
    /// </summary>
    public const string Empty = "fp:none";

    /// <summary>
    /// Fallback key used when no platform secret is configured.
    /// </summary>
    /// <remarks>
    /// Random per process, so fingerprints stay non-reversible even in a misconfigured
    /// deployment. They will not correlate across restarts or instances; that is the intended
    /// trade-off, because leaking recoverable flag material is worse than losing correlation.
    /// </remarks>
    static readonly byte[] FallbackKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Compute a fingerprint for flag material.
    /// </summary>
    /// <param name="value">The flag or submitted answer. May be null or empty.</param>
    /// <param name="key">Platform secret used to key the HMAC.</param>
    /// <returns>A short, stable, non-reversible reference such as <c>fp:1a2b3c4d5e6f7890</c>.</returns>
    public static string Compute(string? value, byte[]? key)
    {
        if (string.IsNullOrEmpty(value))
            return Empty;

        var hmacKey = key is { Length: > 0 } ? key : FallbackKey;

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(hmacKey, Encoding.UTF8.GetBytes(value), hash);

        // 64 bits is ample to distinguish answers within one event while keeping logs readable.
        return string.Create(Prefix.Length + 16, hash[..8].ToArray(), static (span, source) =>
        {
            Prefix.CopyTo(span);
            Convert.TryToHexStringLower(source, span[Prefix.Length..], out _);
        });
    }
}
