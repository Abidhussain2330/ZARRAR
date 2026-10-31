using ZARRAR.Models.Internal;

namespace ZARRAR.Utils;

/// <summary>
/// Resolves the canonical public origin of the platform.
/// </summary>
/// <remarks>
/// Security-sensitive absolute URLs (email verification, password recovery and email-change
/// links) must never be derived from the client-controlled <c>Host</c> header, because a reverse
/// proxy that forwards an arbitrary Host lets an attacker place their own domain into a link that
/// carries a live single-use token. See finding F-001.
///
/// Resolution order:
/// <list type="number">
/// <item>The <c>PublicUrl</c> application setting (appsettings / environment variable). This has
/// the highest precedence because it is operator-controlled and cannot be altered through the
/// in-app administration UI.</item>
/// <item><see cref="GlobalConfig.PublicUrl" />, which an administrator can set at runtime.</item>
/// <item>The request origin — but only when the request host exactly matches an explicit,
/// non-wildcard entry of the <c>AllowedHosts</c> configuration, so the value has already been
/// constrained by the operator.</item>
/// <item>The request origin in the Development environment, for local convenience.</item>
/// </list>
/// When none of these apply the resolver returns <see langword="null" />, and the caller must
/// refuse to emit the link rather than emit a poisoned one.
/// </remarks>
public sealed class PublicOriginResolver
{
    readonly string[] _allowedHosts;
    readonly string? _configuredOrigin;
    readonly bool _isDevelopment;
    readonly ILogger<PublicOriginResolver> _logger;

    public PublicOriginResolver(IConfiguration configuration, IHostEnvironment environment,
        ILogger<PublicOriginResolver> logger)
    {
        _logger = logger;
        _isDevelopment = environment.IsDevelopment();
        _configuredOrigin = Normalize(configuration["PublicUrl"]);

        // AllowedHosts is the stock ASP.NET Core host filtering setting; ";" separated.
        // A "*" entry means "no restriction" and therefore cannot be used to validate a host.
        _allowedHosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(host => !host.Contains('*', StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Whether a canonical origin is configured independently of the incoming request.
    /// </summary>
    public bool HasConfiguredOrigin(GlobalConfig? globalConfig) =>
        _configuredOrigin is not null || Normalize(globalConfig?.PublicUrl) is not null;

    /// <summary>
    /// Resolve the canonical origin, e.g. <c>https://ctf.example.com</c>, without a trailing slash.
    /// </summary>
    /// <param name="globalConfig">Runtime global configuration.</param>
    /// <param name="request">The current request, used only as a constrained fallback.</param>
    /// <returns>The origin, or <see langword="null" /> when none can be trusted.</returns>
    public string? Resolve(GlobalConfig? globalConfig, HttpRequest request)
    {
        if (_configuredOrigin is not null)
            return _configuredOrigin;

        var fromGlobalConfig = Normalize(globalConfig?.PublicUrl);
        if (fromGlobalConfig is not null)
            return fromGlobalConfig;

        var requestHost = request.Host.Value;

        if (!string.IsNullOrEmpty(requestHost) &&
            _allowedHosts.Any(host => string.Equals(host, request.Host.Host, StringComparison.OrdinalIgnoreCase)))
            return $"{request.Scheme}://{requestHost}";

        if (_isDevelopment && !string.IsNullOrEmpty(requestHost))
            return $"{request.Scheme}://{requestHost}";

        _logger.SystemLog(
            StaticLocalizer[nameof(Resources.Program.Config_PublicUrlNotConfigured)],
            TaskStatus.Failed, LogLevel.Error);

        return null;
    }

    /// <summary>
    /// Trim, validate and canonicalise a configured origin value.
    /// </summary>
    /// <remarks>
    /// Only absolute http/https origins are accepted. Any path, query or fragment is discarded so
    /// that a misconfigured value cannot inject additional URL structure into generated links.
    /// </remarks>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return null;

        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
