using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZARRAR.Models.Internal;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ZARRAR.Services.SharedGateway;

public sealed record SharedGatewayTokenClaims(
    string Issuer,
    string Audience,
    int GameId,
    int ChallengeId,
    int ParticipationId,
    int TeamId,
    Guid SessionId,
    long IssuedAt,
    long ExpiresAt,
    Guid Nonce);

/// <summary>Mints short-lived, purpose-bound claims for trusted shared runtimes.</summary>
public sealed class SharedGatewayTokenService(IOptions<SharedGatewayOptions> options)
{
    private readonly SharedGatewayOptions _options = options.Value;

    public bool IsConfigured => Encoding.UTF8.GetByteCount(_options.SigningKey) >= 32;

    public string Mint(string audience, int gameId, int challengeId, int participationId,
        int teamId, Guid sessionId, DateTimeOffset now)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Shared gateway signing key must contain at least 32 UTF-8 bytes.");

        var issuedAt = now.ToUnixTimeSeconds();
        var claims = new SharedGatewayTokenClaims(
            "zarrar-shared-gateway",
            audience,
            gameId,
            challengeId,
            participationId,
            teamId,
            sessionId,
            issuedAt,
            now.AddSeconds(Math.Clamp(_options.TokenSeconds, 15, 300)).ToUnixTimeSeconds(),
            Guid.NewGuid());
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        var encodedPayload = WebEncoders.Base64UrlEncode(payload);
        var signature = Sign(encodedPayload);
        return $"{encodedPayload}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public bool TryValidate(string token, string expectedAudience, DateTimeOffset now,
        out SharedGatewayTokenClaims? claims)
    {
        claims = null;
        if (!IsConfigured || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;

        byte[] presented;
        byte[] payload;
        try
        {
            presented = WebEncoders.Base64UrlDecode(parts[1]);
            payload = WebEncoders.Base64UrlDecode(parts[0]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign(parts[0]);
        if (presented.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(presented, expected))
            return false;

        try
        {
            claims = JsonSerializer.Deserialize<SharedGatewayTokenClaims>(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        var timestamp = now.ToUnixTimeSeconds();
        if (claims is null || claims.Issuer != "zarrar-shared-gateway" ||
            !string.Equals(claims.Audience, expectedAudience, StringComparison.Ordinal) ||
            claims.ExpiresAt <= timestamp || claims.IssuedAt > timestamp + 5 ||
            claims.ExpiresAt - claims.IssuedAt > 300)
        {
            claims = null;
            return false;
        }

        return true;
    }

    private byte[] Sign(string encodedPayload) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SigningKey), Encoding.UTF8.GetBytes(encodedPayload));
}
