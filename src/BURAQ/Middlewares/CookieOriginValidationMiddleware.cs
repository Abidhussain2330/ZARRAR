using System.Net.Mime;
using Microsoft.Extensions.Localization;

namespace BURAQ.Middlewares;

/// <summary>
/// Rejects cookie-authenticated state changes that arrive from a foreign origin.
/// </summary>
/// <remarks>
/// The platform authenticates browsers with a cookie but shipped no antiforgery tokens and no
/// origin validation, leaving CSRF defence entirely to the browser's SameSite default
/// (finding F-023). That default is not an invariant: it varies by browser, it does not constrain
/// a compromised sibling subdomain, and it silently disappears if the cookie policy is ever
/// widened.
///
/// This middleware makes the invariant explicit. It checks Origin (falling back to Referer) on
/// unsafe methods and WebSocket upgrades for requests that actually carry the identity cookie. In Development only, an
/// exact localhost origin may also be trusted through <c>Development:TrustedOrigins</c>; this
/// supports the Vite development proxy without changing the production same-origin rule.
///
/// Requests with no Origin and no Referer are allowed through: browsers always attach Origin to
/// cross-origin requests, so their absence indicates a non-browser client (API token automation,
/// CLI tooling, server-to-server), which is not subject to CSRF and must keep working.
/// </remarks>
public sealed class CookieOriginValidationMiddleware(RequestDelegate next)
{
    const string IdentityCookieName = "BURAQ_Token";
    const string InvalidOrigin = "\0";

    static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace
    };

    public async Task InvokeAsync(HttpContext context, IStringLocalizer<Program> localizer,
        IHostEnvironment environment, IConfiguration configuration)
    {
        if (IsPermitted(context, environment, configuration))
        {
            await next(context);
            return;
        }

        // A cookie-carrying request from a foreign origin is a CSRF / cross-site WebSocket attempt
        // (or a misconfigured proxy): either way the SOC should see it.
        context.RequestServices.GetService<BURAQ.Services.Soc.SecuritySignalSink>()?.Signal(
            // Keyed by origin + address only, so varying the path can't multiply the events.
            $"cross-origin:{Clip(context.Request.Headers.Origin.ToString())}:{context.Connection.RemoteIpAddress}",
            new BURAQ.Services.Soc.SocEventMessage(
                EventType: "CrossOriginRequestBlocked",
                Category: SocCategory.PlatformSecurity,
                Severity: SocSeverity.Medium,
                Details: "A cookie-authenticated request from a foreign origin was refused (possible CSRF or cross-site WebSocket hijacking).",
                Source: nameof(CookieOriginValidationMiddleware),
                UserId: Guid.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    out var victim) ? victim : null,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    origin = Clip(context.Request.Headers.Origin.ToString()),
                    referer = Clip(context.Request.Headers.Referer.ToString()),
                    method = context.Request.Method,
                    path = Clip(context.Request.Path.Value ?? string.Empty),
                    address = context.Connection.RemoteIpAddress?.ToString()
                })));

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        await context.Response.WriteAsJsonAsync(
            new RequestResponse(localizer[nameof(Resources.Program.Auth_CrossOriginDenied)],
                StatusCodes.Status403Forbidden));
    }

    static bool IsPermitted(HttpContext context, IHostEnvironment environment,
        IConfiguration configuration)
    {
        // A WebSocket upgrade is a GET, but it opens a two-way channel that carries the cookie's
        // authority (screen publish/watch, SignalR hubs, the container proxy). Browsers always
        // send Origin on it, so it gets the same check as an unsafe method (cross-site WebSocket
        // hijacking).
        // (Read from the Upgrade header: this middleware runs before UseWebSockets, so the
        // WebSocket feature is not populated yet. HTTP/2 WebSockets use CONNECT, already unsafe.)
        if (SafeMethods.Contains(context.Request.Method) && !IsWebSocketUpgrade(context.Request))
            return true;

        // Only cookie-borne authority is at risk. A bearer/API-token request cannot be forged by
        // a foreign page, because the browser will not attach the credential for it.
        if (!context.Request.Cookies.ContainsKey(IdentityCookieName))
            return true;

        var claimed = GetClaimedOrigin(context.Request);

        // No Origin and no Referer: not a browser-initiated cross-site request.
        if (claimed is null)
            return true;

        if (string.Equals(claimed, GetSelfOrigin(context.Request), StringComparison.OrdinalIgnoreCase))
            return true;

        return environment.IsDevelopment() &&
               IsConfiguredDevelopmentOrigin(claimed, configuration);
    }

    static string Clip(string value) => value.Length <= 200 ? value : value[..200];

    static bool IsWebSocketUpgrade(HttpRequest request)
    {
        foreach (var value in request.Headers.Upgrade)
            if (value is not null && value.Contains("websocket", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    static bool IsConfiguredDevelopmentOrigin(string claimed, IConfiguration configuration)
    {
        foreach (var entry in configuration.GetSection("Development:TrustedOrigins").GetChildren())
        {
            if (TryNormalizeLocalhostOrigin(entry.Value, out var trusted) &&
                string.Equals(claimed, trusted, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool TryNormalizeLocalhostOrigin(string? value, out string origin)
    {
        origin = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        origin = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        return true;
    }

    static string? GetClaimedOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();

        // An opaque Origin (the literal "null"), a multi-value header, or malformed origin is
        // still browser-originated and must fail closed. Treating it like an absent header would
        // let sandboxed/opaque-origin requests bypass the CSRF check.
        if (!string.IsNullOrEmpty(origin))
            return TryNormalizeOrigin(origin, allowPath: false, out var normalizedOrigin)
                ? normalizedOrigin
                : InvalidOrigin;

        var referer = request.Headers.Referer.ToString();

        if (string.IsNullOrEmpty(referer))
            return null;

        return TryNormalizeOrigin(referer, allowPath: true, out var normalizedReferer)
            ? normalizedReferer
            : InvalidOrigin;
    }

    static bool TryNormalizeOrigin(string value, bool allowPath, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            (!allowPath && (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
                            !string.IsNullOrEmpty(uri.Fragment))))
            return false;

        origin = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return true;
    }

    /// <summary>
    /// The origin the request was addressed to, as the browser would have computed it.
    /// </summary>
    /// <remarks>
    /// Uses the request's own scheme and host rather than a configured value. This is safe here in
    /// a way it is not for email links (finding F-001): a spoofed Host changes both sides of the
    /// comparison identically and so cannot turn a foreign origin into a match.
    /// </remarks>
    static string GetSelfOrigin(HttpRequest request) => $"{request.Scheme}://{request.Host.Value}";
}
