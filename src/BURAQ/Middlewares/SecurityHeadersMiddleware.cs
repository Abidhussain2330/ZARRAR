namespace BURAQ.Middlewares;

/// <summary>
/// Applies the baseline browser security headers to every response.
/// </summary>
/// <remarks>
/// The Content-Security-Policy was previously emitted only by the SPA fallback handler, so API
/// responses, error pages, static assets and proxy responses carried no browser policy at all, and
/// the repository contained no X-Content-Type-Options, Referrer-Policy or Permissions-Policy
/// anywhere (finding F-015).
///
/// These headers are set here rather than in the fallback handler so that coverage does not depend
/// on which endpoint happens to serve a response. The per-request nonce-bearing CSP is still
/// written by the SPA handler, and is deliberately not overwritten here.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Deny framing outright. Paired with the CSP frame-ancestors directive for browsers that
    /// still honour only the legacy header.
    /// </summary>
    const string FrameOptions = "DENY";

    /// <summary>
    /// Stop content-type sniffing, which otherwise lets an uploaded file be reinterpreted as an
    /// active document regardless of the declared type.
    /// </summary>
    const string ContentTypeOptions = "nosniff";

    /// <summary>
    /// Send the full URL only to same-origin destinations. Challenge and team URLs frequently
    /// carry identifiers that should not leak to third-party sites through the Referer header.
    /// </summary>
    const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>
    /// Drop capabilities the platform never uses, so injected content cannot request them.
    /// </summary>
    const string PermissionsPolicy =
        // ZARRAR: display-capture is allowed for this origin so players can stream their screen
        // to the stage, and autoplay so the stage can play its flag sound effects.
        "accelerometer=(), autoplay=(self), camera=(), display-capture=(self), encrypted-media=(), " +
        "geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), " +
        "usb=(), xr-spatial-tracking=()";

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting so the headers survive handlers that write directly to the response, and so
        // they are applied exactly once, immediately before the status line is committed.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;

            headers.XContentTypeOptions = ContentTypeOptions;
            headers.XFrameOptions = FrameOptions;
            headers["Referrer-Policy"] = ReferrerPolicy;
            headers["Permissions-Policy"] = PermissionsPolicy;

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
