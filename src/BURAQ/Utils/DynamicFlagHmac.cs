using System.Security.Cryptography;
using System.Text;

namespace BURAQ.Utils;

/// <summary>
/// Generates opaque, instance-bound flags for dynamic container challenges.
/// The identifiers are authenticated by a deployment secret and are never embedded in plaintext.
/// </summary>
public sealed class DynamicFlagHmac(IConfiguration configuration) : IDisposable
{
    private const string ConfigurationKey = "DynamicFlagHmacKey";
    private const string Domain = "buraq-dynamic-flag:v1";
    private readonly byte[] _key = LoadKey(configuration[ConfigurationKey]);

    public string Generate(int gameId, int challengeId, int teamId, int participationId, Guid flagInstanceId)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{Domain}|game={gameId}|challenge={challengeId}|team={teamId}|participation={participationId}|instance={flagInstanceId:N}");
        var digest = HMACSHA256.HashData(_key, payload);

        // Full 256-bit output: predictable format, no identifiers or secret material are exposed.
        return $"BRQ{{{Convert.ToHexString(digest)}}}";
    }

    private static byte[] LoadKey(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException($"{ConfigurationKey} is required.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{ConfigurationKey} must be Base64 encoded.", exception);
        }

        if (key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException($"{ConfigurationKey} must contain at least 32 random bytes.");
        }

        return key;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
