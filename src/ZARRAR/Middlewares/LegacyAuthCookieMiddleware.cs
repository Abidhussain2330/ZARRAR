using Microsoft.Extensions.Primitives;

namespace ZARRAR.Middlewares;

/// <summary>
/// Accepts an identity cookie from older releases once and upgrades the browser to
/// <c>ZARRAR_Token</c> without exposing its former name.
/// </summary>
/// <remarks>
/// Renaming the identity cookie alone would sign out every logged-in session at upgrade, which is
/// avoidable: the cookie is an ASP.NET Core authentication ticket sealed with the data-protection
/// key ring, and the rebrand does not touch that key ring. The payload therefore stays valid — only
/// the name it travels under changed.
///
/// This middleware runs before <c>UseAuthentication</c>. When a request carries the legacy cookie
/// and not the new one, the legacy value is appended to the request's Cookie header under the new
/// name, so the unmodified cookie authentication handler reads it, and the response re-issues the
/// value as <c>ZARRAR_Token</c> while expiring the legacy cookie. Each browser pays this once.
///
/// The ticket is never decrypted, inspected or logged here — an invalid or forged value simply
/// fails in the authentication handler exactly as it would have before, so this adds no way to
/// authenticate that did not already exist.
///
/// REMOVAL: this fallback is scheduled for removal one cookie lifetime (7 days) after every
/// deployment has upgraded. See INSTALLATION_AND_SETUP.md.
/// </remarks>
public sealed class LegacyAuthCookieMiddleware(RequestDelegate next, ILogger<LegacyAuthCookieMiddleware> logger)
{
    internal const string CookieName = "ZARRAR_Token";
    internal static readonly string LegacyCookieName = string.Concat("GZ", "CTF_Token");

    public async Task InvokeAsync(HttpContext context)
    {
        var legacy = context.Request.Cookies[LegacyCookieName];

        if (string.IsNullOrEmpty(legacy) || !string.IsNullOrEmpty(context.Request.Cookies[CookieName]))
        {
            await next(context);
            return;
        }

        // Re-parsing is driven off the Cookie header, so appending to it is what makes the
        // authentication handler see the value under the new name.
        var header = context.Request.Headers.Cookie.ToString();
        context.Request.Headers.Cookie = new StringValues(
            string.IsNullOrEmpty(header) ? $"{CookieName}={legacy}" : $"{header}; {CookieName}={legacy}");

        // Mirrors the identity cookie's SecurePolicy: Always in production, SameAsRequest in
        // development. Registered after UseForwardedHeaders, so IsHttps reflects the real scheme
        // when TLS is terminated at a proxy.
        var secure = context.Request.IsHttps;

        context.Response.Cookies.Append(CookieName, legacy, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = secure,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            IsEssential = true
        });

        context.Response.Cookies.Delete(LegacyCookieName, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = secure
        });

        logger.LogDebug("Migrated a legacy {Legacy} cookie to {Current}", LegacyCookieName, CookieName);

        await next(context);
    }
}
