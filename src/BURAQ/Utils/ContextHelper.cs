using System.Security.Claims;
using BURAQ.Services.Token;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace BURAQ.Utils;

public static class ContextHelper
{
    private static readonly CacheControlHeaderValue NoCacheHeader = new() { NoCache = true, MustRevalidate = true };

    private static readonly CacheControlHeaderValue PrivateNoCacheHeader =
        new() { NoCache = true, Private = true, MustRevalidate = true };

    public static EntityTagHeaderValue SetCacheHeaders(HttpResponse response, StringSegment eTag,
        DateTimeOffset lastModified, bool isPrivate = false, bool isWeak = true)
    {
        var headers = response.GetTypedHeaders();
        headers.ETag = new EntityTagHeaderValue(eTag, isWeak);
        headers.LastModified = lastModified;
        headers.CacheControl = isPrivate ? PrivateNoCacheHeader : NoCacheHeader;
        return headers.ETag;
    }

    public static bool IsNotModified(HttpRequest request, HttpResponse response, StringSegment eTag,
        DateTimeOffset lastModified, bool isPrivate = false, bool isWeak = true)
    {
        var eTagValue = SetCacheHeaders(response, eTag, lastModified, isPrivate, isWeak);
        var headers = request.GetTypedHeaders();

        var ifNoneMatch = headers.IfNoneMatch;
        if (ifNoneMatch.Count > 0)
        {
            return ifNoneMatch.Any(tag =>
                StringSegment.Equals(tag.Tag, "*", StringComparison.Ordinal) ||
                tag.Compare(eTagValue, !isWeak));
        }

        if (headers.IfModifiedSince is { } ifModifiedSince)
        {
            return lastModified <= ifModifiedSince;
        }

        // No conditional headers, so the resource is considered modified.
        return false;
    }

    /// <summary>
    /// Checks if the current request has the specified privilege.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="privilege">The privilege to check against.</param>
    /// <returns></returns>
    private static async Task<bool> HasPrivilege(HttpContext context, Role privilege)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null || !Guid.TryParse(userId, out var id))
            return false;

        var dbContext = context.RequestServices.GetRequiredService<AppDbContext>();
        return await dbContext.Users.AnyAsync(i => i.Id == id && i.Role >= privilege);
    }

    /// <summary>
    /// Resolves the API token presented by the current request, if any.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>
    /// The stored token when the bearer credential is valid, null otherwise. The returned token is
    /// authenticated but not yet authorised: the caller must confirm that
    /// <see cref="ApiToken.Scope" /> covers the capability it is about to grant.
    /// </returns>
    public static async Task<ApiToken?> GetValidToken(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.FirstOrDefault()?.Split(' ');

        if (value is not ["Bearer", { Length: > 0 } token])
            return null;

        var tokenService = context.RequestServices.GetRequiredService<ITokenService>();
        return await tokenService.ValidateToken(token);
    }

    /// <summary>
    /// Checks if the current request carries a valid token that covers the required scope.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="required">
    /// The capability the caller is about to grant. There is no default: an endpoint has to name
    /// the scope it needs, so a token is never accepted merely for being valid (finding F-012).
    /// </param>
    /// <returns></returns>
    public static async Task<bool> HasValidToken(HttpContext context, TokenScope required)
    {
        if (required is TokenScope.None)
            return false;

        var apiToken = await GetValidToken(context);
        return apiToken is not null && apiToken.Scope.Covers(required);
    }

    /// <summary>
    /// Checks if the current request has <see cref="Role.Admin" /> privileges.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns></returns>
    public static Task<bool> HasAdmin(HttpContext context) => HasPrivilege(context, Role.Admin);

    /// <summary>
    /// Checks if the current request has <see cref="Role.Monitor" /> privileges.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns></returns>
    public static Task<bool> HasMonitor(HttpContext context) => HasPrivilege(context, Role.Monitor);

    /// <summary>
    /// Checks if the current request has <see cref="Role.User" /> privileges.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns></returns>
    public static Task<bool> HasUser(HttpContext context) => HasPrivilege(context, Role.User);
}
