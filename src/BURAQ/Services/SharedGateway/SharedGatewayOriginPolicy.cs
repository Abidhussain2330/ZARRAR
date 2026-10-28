using BURAQ.Models.Internal;
using Microsoft.Extensions.Options;

namespace BURAQ.Services.SharedGateway;

/// <summary>Exact origin allowlist that prevents an administrator-facing gateway from becoming an SSRF primitive.</summary>
public sealed class SharedGatewayOriginPolicy(IOptions<SharedGatewayOptions> options)
{
    private readonly SharedGatewayOptions _options = options.Value;

    public bool TryValidate(string value, out Uri? origin, out string? error)
    {
        origin = null;
        error = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            candidate.AbsolutePath != "/")
        {
            error = "Origin must be an absolute authority URL without credentials, path, query, or fragment.";
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps &&
            !(_options.AllowHttp && candidate.Scheme == Uri.UriSchemeHttp))
        {
            error = "Shared runtime origins must use HTTPS unless AllowHttp is explicitly enabled.";
            return false;
        }

        var normalized = Normalize(candidate);
        var allowed = _options.AllowedOrigins.Any(item =>
            Uri.TryCreate(item, UriKind.Absolute, out var configured) &&
            string.Equals(Normalize(configured), normalized, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            error = "Origin is not present in SharedGatewayOptions:AllowedOrigins.";
            return false;
        }

        origin = new Uri(normalized, UriKind.Absolute);
        return true;
    }

    private static string Normalize(Uri uri) => uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";
}
