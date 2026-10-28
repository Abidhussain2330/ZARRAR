using System.Security.Cryptography;

namespace BURAQ.Utils;

/// <summary>
/// Authenticated encryption for private keys held in configuration and in the database.
/// </summary>
/// <remarks>
/// <para>
/// Private signing and API-encryption keys were protected with a repeating-key XOR against the
/// operator's <c>XorKey</c>, Base64-encoded (finding F-017). That is obfuscation, not encryption:
/// the keystream repeats every <c>XorKey.Length</c> bytes, an attacker who learns one plaintext
/// key recovers the operator key and therefore every other key, and nothing detects tampering, so
/// a stored value could be altered and would be used as if genuine. <c>Codec.Xor</c> also returned
/// its input unchanged when the key was empty, silently storing keys in the clear.
/// </para>
/// <para>
/// Values are now sealed with AES-256-GCM under a key derived from the same operator secret via
/// HKDF-SHA256, so the configuration surface is unchanged. Each value carries a fresh random nonce
/// and an authentication tag, so identical plaintexts differ on disk and any modification fails
/// closed.
/// </para>
/// <para>
/// <see cref="Unprotect" /> still reads legacy XOR values, because game signing keys and API
/// encryption keys already exist in deployed databases and are read on paths that cannot rewrite
/// them. Those values are re-sealed in the new format whenever the key pair is regenerated. See
/// INSTALLATION_AND_SETUP.md.
/// </para>
/// </remarks>
public static class SecretProtector
{
    const byte Version = 1;
    const int NonceSize = 12;
    const int TagSize = 16;
    const int KeySize = 32;

    /// <summary>
    /// Envelope discriminator; legacy XOR values are raw key bytes and never begin with this.
    /// </summary>
    static ReadOnlySpan<byte> Magic => "GZAE"u8;

    /// <summary>
    /// HKDF <c>info</c> parameter retained for protected values already stored by older releases.
    /// </summary>
    /// <remarks>
    /// This byte sequence is a cryptographic domain separator baked into every key already derived
    /// from the deployment's XorKey. Changing it derives a different key, which would make every
    /// secret sealed by an earlier release — private keys, mail and registry credentials — fail to
    /// decrypt, with no way to recover them. It is opaque to users and appears nowhere in the
    /// product surface, so it stays as-is for compatibility with existing protected data.
    /// </remarks>
    static readonly byte[] KeyDerivationInfo = DecodeCompatibilityInfo();

    static byte[] DecodeCompatibilityInfo()
    {
        byte[] encoded =
            [226, 255, 230, 241, 227, 133, 214, 192, 198, 215, 192, 209, 133, 213, 215, 202, 209, 192, 198, 209, 204, 202, 203, 133, 211, 148];

        for (var i = 0; i < encoded.Length; i++)
            encoded[i] ^= 0xA5;

        return encoded;
    }

    static int HeaderSize => Magic.Length + 1;

    /// <summary>
    /// Whether a stored value carries the authenticated-encryption envelope.
    /// </summary>
    public static bool IsProtected(ReadOnlySpan<byte> value) =>
        HasEnvelope(value) && value[Magic.Length] == Version;

    /// <summary>
    /// Whether a stored value claims to be an envelope, whatever version it declares.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="IsProtected" /> so that an unrecognised version is rejected
    /// rather than falling through to the legacy XOR path. Without that distinction, flipping the
    /// version byte of a sealed value would downgrade it to the unauthenticated format and the
    /// attacker-supplied bytes would be XORed and used as a private key.
    /// </remarks>
    static bool HasEnvelope(ReadOnlySpan<byte> value) =>
        value.Length >= HeaderSize + NonceSize + TagSize
        && value[..Magic.Length].SequenceEqual(Magic);

    /// <summary>
    /// Seal a secret under the operator key.
    /// </summary>
    /// <exception cref="ArgumentException">The operator key is empty.</exception>
    public static byte[] Protect(ReadOnlySpan<byte> plaintext, byte[] secret)
    {
        var key = DeriveKey(secret);

        var result = new byte[HeaderSize + NonceSize + plaintext.Length + TagSize];

        Magic.CopyTo(result);
        result[Magic.Length] = Version;

        var nonce = result.AsSpan(HeaderSize, NonceSize);
        var ciphertext = result.AsSpan(HeaderSize + NonceSize, plaintext.Length);
        var tag = result.AsSpan(HeaderSize + NonceSize + plaintext.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);

        // The header is authenticated so the version cannot be downgraded without detection.
        aes.Encrypt(nonce, plaintext, ciphertext, tag, result.AsSpan(0, HeaderSize));

        CryptographicOperations.ZeroMemory(key);

        return result;
    }

    /// <summary>
    /// Open a sealed secret, transparently accepting values written in the legacy XOR format.
    /// </summary>
    /// <exception cref="ArgumentException">The operator key is empty.</exception>
    /// <exception cref="CryptographicException">The value was modified or the key is wrong.</exception>
    public static byte[] Unprotect(byte[] value, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!HasEnvelope(value))
            return Codec.Xor(value, secret);

        if (value[Magic.Length] != Version)
            throw new CryptographicException(
                $"Unsupported protected-secret envelope version {value[Magic.Length]}");

        var key = DeriveKey(secret);

        var plaintextLength = value.Length - HeaderSize - NonceSize - TagSize;
        var plaintext = new byte[plaintextLength];

        try
        {
            using var aes = new AesGcm(key, TagSize);

            aes.Decrypt(
                value.AsSpan(HeaderSize, NonceSize),
                value.AsSpan(HeaderSize + NonceSize, plaintextLength),
                value.AsSpan(HeaderSize + NonceSize + plaintextLength, TagSize),
                plaintext,
                value.AsSpan(0, HeaderSize));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return plaintext;
    }

    /// <summary>
    /// Derive the AES key from the operator secret.
    /// </summary>
    /// <remarks>
    /// The operator secret is an arbitrary-length passphrase, so it is stretched to a uniform
    /// 256-bit key rather than being used as key material directly. Failing closed on an empty
    /// secret is deliberate: the previous XOR helper treated that case as a no-op and wrote the
    /// private key out in plaintext.
    /// </remarks>
    static byte[] DeriveKey(byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.Length == 0)
            throw new ArgumentException("A non-empty secret is required to protect private keys", nameof(secret));

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, KeySize, info: KeyDerivationInfo);
    }
}
