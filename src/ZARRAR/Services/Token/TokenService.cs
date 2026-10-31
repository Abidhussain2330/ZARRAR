using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Config;
using Microsoft.IdentityModel.Tokens;

namespace ZARRAR.Services.Token;

/// <summary>
/// Service for handling token generation and validation.
/// </summary>
public class TokenService(IConfigService configService, IApiTokenRepository apiTokenRepository) : ITokenService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public async Task<string> GenerateToken(ApiToken apiToken, CancellationToken cancellationToken = default)
    {
        var ctx = await configService.GetApiTokenContext(cancellationToken);

        // Expiry is mandatory: every issued credential carries a bounded lifetime, so a leaked
        // token string cannot be replayed forever even if its row is never revoked.
        Dictionary<string, object> payloads = new(3)
        {
            { JwtRegisteredClaimNames.Jti, apiToken.Id.ToString() },
            { JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            { JwtRegisteredClaimNames.Exp, apiToken.ExpiresAt.ToUnixTimeSeconds() }
        };

        var result = Base64UrlEncoder.Encode(JsonSerializer.Serialize(payloads, Options));
        var signature = ctx.Sign(result);

        return $"{result}.{signature}";
    }

    public async Task<ApiToken?> ValidateToken(string token, CancellationToken cancellationToken = default)
    {
        if (token.Split('.') is not [var base64Payload, var signature])
            return null;

        if (string.IsNullOrWhiteSpace(base64Payload) || string.IsNullOrWhiteSpace(signature))
            return null;

        Dictionary<string, JsonElement>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                Base64UrlEncoder.Decode(base64Payload));
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            // The payload is entirely attacker-controlled, so a malformed encoding is a rejected
            // credential rather than a server fault. Only decoding failures are absorbed here;
            // anything else (database, configuration, cryptography) is a real failure and must
            // propagate instead of being reported as "invalid token".
            return null;
        }

        if (payload is null
            || !payload.TryGetValue(JwtRegisteredClaimNames.Jti, out var jtiElement)
            || jtiElement.ValueKind != JsonValueKind.String)
            return null;

        if (!Guid.TryParse(jtiElement.GetString(), out var jti))
            return null;

        var apiToken = await apiTokenRepository.GetTokenByIdAsync(jti, cancellationToken);
        if (apiToken is null || apiToken.IsRevoked)
            return null;

        var now = DateTimeOffset.UtcNow;
        if (apiToken.ExpiresAt <= now)
            return null;

        // The presented credential must carry its own unexpired exp claim as well, so shortening a
        // token's stored lifetime takes effect immediately and a payload can never outlive the
        // lifetime it was signed with.
        if (!payload.TryGetValue(JwtRegisteredClaimNames.Exp, out var expElement)
            || expElement.ValueKind != JsonValueKind.Number
            || !expElement.TryGetInt64(out var exp)
            || exp <= now.ToUnixTimeSeconds())
            return null;

        var ctx = await configService.GetApiTokenContext(cancellationToken);
        if (!ctx.Verify(base64Payload, signature))
            return null;

        // A grant of TokenScope.None, or one carrying bits that no longer map to a capability, is
        // not a usable credential. The caller still has to check the scope the endpoint requires.
        if (!apiToken.Scope.IsValidGrant())
            return null;

        await apiTokenRepository.RecordTokenUsageAsync(apiToken.Id, cancellationToken);

        return apiToken;
    }
}
