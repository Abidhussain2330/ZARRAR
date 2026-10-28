using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BURAQ.Models.Internal;
using BURAQ.Providers;
using BURAQ.Services.Cache;
using BURAQ.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace BURAQ.Extensions;

public static class HandlerExtension
{
    private const string CspTemplatePrefix = "default-src 'strict-dynamic' 'nonce-";

    // frame-ancestors and form-action were both absent, leaving every page framable and any
    // injected form free to post off-origin (finding F-015). img-src/font-src/frame-src stay
    // permissive on purpose: avatars, challenge artwork and challenge instances are legitimately
    // served from operator-chosen origins, and tightening them here would break real events.
    // Operators must treat those deliberately broad sources as an accepted deployment risk.
    private const string CspTemplateSuffix = "' 'unsafe-inline' http: https:; " +
                                             "style-src 'self' 'unsafe-inline'; img-src * 'self' data: blob:; " +
                                             "font-src * 'self' data:; object-src 'none'; frame-src * https:; " +
                                             "connect-src 'self' http://127.0.0.1:*; base-uri 'none'; " +
                                             "frame-ancestors 'none'; form-action 'self';";

    private static readonly int CspHeaderLength = CspTemplatePrefix.Length + 12 + CspTemplateSuffix.Length;

    private static string GetContentSecurityPolicy(ReadOnlySpan<char> nonce)
    {
        var builder = new StringBuilder(CspHeaderLength);
        builder.Append(CspTemplatePrefix);
        builder.Append(nonce);
        builder.Append(CspTemplateSuffix);
        return builder.ToString();
    }

    private static readonly DistributedCacheEntryOptions
        StaticCacheOptions = new() { SlidingExpiration = TimeSpan.FromDays(7) };

    private static readonly HashSet<string> SupportedCultures = Server.SupportedCultures
        .Select(c => c.ToLower()).ToHashSet();

    private static readonly HashSet<string> ShortSupportedCultures = SupportedCultures
        .Select(c => c.Split('-')[0]).ToHashSet();

    private static readonly string NoCacheHeaderValue = new CacheControlHeaderValue
    {
        NoCache = true,
        NoStore = true,
        MustRevalidate = true,
        MaxAge = TimeSpan.Zero
    }.ToString();

    private static string _indexTemplate = string.Empty;
    private static string _adminIndexTemplate = string.Empty;

    extension(IConfigurationBuilder builder)
    {
        public void AddEntityConfiguration(Action<DbContextOptionsBuilder> optionsAction) =>
            builder.Add(new EntityConfigurationSource(optionsAction));
    }

    extension(WebApplication app)
    {
        public void UseCustomFavicon() =>
            app.MapGet("/favicon.webp", FaviconHandler);

        /// <summary>
        /// Registers the single SPA fallback for the whole site.
        /// </summary>
        /// <remarks>
        /// There is exactly ONE MapFallback call site in this file, and that is load-bearing rather
        /// than stylistic. The ASP.NET Core route interceptor source generator cross-wires multiple
        /// MapFallback call sites: with two, it first refused to compile (CS9144), and once that was
        /// worked around it silently registered BOTH call sites under the same route pattern, so
        /// every incoming request matched two identical endpoints and the router threw
        /// AmbiguousMatchException — at request time, not at startup, so the app looked healthy and
        /// then returned HTTP 500 for every page. One call site cannot be cross-wired.
        ///
        /// This method therefore replaces what used to be UseAdminIndexAsync + UseIndexAsync (four
        /// call sites between them). Which template gets served is decided here, once, and the
        /// chosen handler is what gets registered:
        ///
        ///   wwwroot/admin/index.html — the React bundle, which now serves the entire site: the
        ///     landing page, login, registration, /games, /teams, /posts and /admin alike.
        ///   wwwroot/index.html — legacy standalone index, used only when the bundle is absent.
        ///   neither — plain 404, rather than a null-reference at request time.
        ///
        /// Static files are unaffected: UseStaticFiles runs earlier in the pipeline, so anything
        /// physically present under wwwroot/ (assets, the bundle's own JS and CSS) is served
        /// directly and never reaches this handler.
        /// </remarks>
        public void UseSpaFallback()
        {
            // Pick the handler first, register it once. The single MapFallback call below is the
            // only one in this file, for the reason given in the remarks above; selecting into a
            // Delegate keeps it that way instead of branching into two or three call sites.
            Delegate handler;

            var adminIndex = app.Environment.WebRootFileProvider.GetFileInfo("admin/index.html");
            var index = app.Environment.WebRootFileProvider.GetFileInfo("index.html");

            if (adminIndex.Exists && adminIndex.PhysicalPath is not null)
            {
                _adminIndexTemplate = File.ReadAllText(adminIndex.PhysicalPath);
                handler = AdminIndexHandler;
            }
            else if (index.Exists && index.PhysicalPath is not null)
            {
                _indexTemplate = File.ReadAllText(index.PhysicalPath);
                handler = IndexHandler;
            }
            else
            {
                handler = NotFoundHandler;
            }

#pragma warning disable RDG002 // The selected static handler intentionally falls back to runtime generation.
            app.MapFallback("{*path:nonfile}", handler);
#pragma warning restore RDG002
        }
    }

    private static string GetETag(StringSegment hash) => $"\"favicon-{hash}\"";

    private static async Task<IResult> FaviconHandler(
        CacheHelper cache,
        HttpContext context,
        IBlobStorage storage,
        IOptionsSnapshot<GlobalConfig> globalConfig,
        CancellationToken token = default)
    {
        var hash = await cache.GetStringAsync(CacheKey.Favicon, token);
        if (hash is null)
        {
            hash = globalConfig.Value.FaviconHash;
            if (hash is null || !Codec.FileHashRegex().IsMatch(hash))
                goto FallbackToDefaultIcon;
        }

        var eTag = GetETag(hash[..8]);
        if (context.Request.Headers.IfNoneMatch == eTag)
            return Results.StatusCode(StatusCodes.Status304NotModified);

        if (hash == Program.DefaultFaviconHash)
            goto SendDefaultIcon;

        var path = StoragePath.Combine(PathHelper.Uploads, hash[..2], hash[2..4], hash);
        if (!await storage.ExistsAsync(path, token))
            goto FallbackToDefaultIcon;

        await cache.SetStringAsync(CacheKey.Favicon, hash, StaticCacheOptions, token);
        var stream = await storage.OpenReadAsync(path, token);

        return Results.File(
            stream,
            MediaTypeNames.Image.Webp,
            "favicon.webp",
            entityTag: EntityTagHeaderValue.Parse(eTag));

    FallbackToDefaultIcon:

        eTag = GetETag(Program.DefaultFaviconHash[..8]);
        await cache.SetStringAsync(CacheKey.Favicon, Program.DefaultFaviconHash, StaticCacheOptions, token);

    SendDefaultIcon:

        return Results.File(
            Program.DefaultFavicon,
            MediaTypeNames.Image.Webp,
            "favicon.webp",
            entityTag: EntityTagHeaderValue.Parse(eTag));
    }

    private static string ExtractLanguage(StringValues acceptLanguage)
    {
        if (StringValues.IsNullOrEmpty(acceptLanguage))
            return "en-us";

        var input = acceptLanguage.ToString();
        if (input.Length > 100)
            return "en-us";

        var tokenizer = new StringTokenizer(input, [',']);
        foreach (var segment in tokenizer)
        {
            var entry = segment;
            if (!entry.HasValue)
                continue;

            var semiIndex = entry.IndexOf(';');
            if (semiIndex >= 0)
                entry = entry.Subsegment(0, semiIndex);

            entry = entry.Trim();
            if (!entry.HasValue)
                continue;

            var culture = entry.ToString().ToLowerInvariant();

            if (SupportedCultures.Contains(culture))
                return culture;

            if (culture == "*")
                return "en-us";

            var dashIndex = culture.IndexOf('-');
            if (dashIndex <= 0)
                continue;

            var shortCulture = culture[..dashIndex];
            if (ShortSupportedCultures.Contains(shortCulture))
                return shortCulture;
        }

        return "en-us";
    }

    private static async Task<IResult> IndexHandler(
        CacheHelper cacheHelper,
        HttpContext context,
        IOptionsSnapshot<GlobalConfig> globalConfig,
        CancellationToken token = default) =>
        await RenderSpaIndexAsync(_indexTemplate, CacheKey.Index, cacheHelper, context, globalConfig, token);

    /// <summary>Last-resort fallback when no SPA index exists in wwwroot at all.</summary>
    private static IResult NotFoundHandler() =>
        Results.Content(string.Empty, MediaTypeNames.Text.Html, statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> AdminIndexHandler(
        CacheHelper cacheHelper,
        HttpContext context,
        IOptionsSnapshot<GlobalConfig> globalConfig,
        CancellationToken token = default) =>
        await RenderSpaIndexAsync(_adminIndexTemplate, CacheKey.AdminIndex, cacheHelper, context, globalConfig, token);

    private static async Task<IResult> RenderSpaIndexAsync(
        string template,
        string cacheKey,
        CacheHelper cacheHelper,
        HttpContext context,
        IOptionsSnapshot<GlobalConfig> globalConfig,
        CancellationToken token)
    {
        if (context.Request.Method != HttpMethods.Get && context.Request.Method != HttpMethods.Head)
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

        // The generated SPA asset names are content-hashed and change on every frontend build.
        // Redis survives application deployments, so a stable cache key can otherwise return an
        // older index that points at bundles no longer present in wwwroot, producing a blank page.
        // Include the packaged template hash in the key so each deployment can only reuse its own
        // rendered page shell.
        var templateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..16];
        cacheKey = $"{cacheKey}:{templateHash}";

        var content = await cacheHelper.GetStringAsync(cacheKey, token);

        if (content is null)
        {
            var config = globalConfig.Value;
            var title = HtmlEncoder.Default.Encode(config.Platform);
            var description = HtmlEncoder.Default.Encode(config.Description ?? GlobalConfig.DefaultDescription);
            content = template.Replace("%title%", title).Replace("%description%", description);

            await cacheHelper.SetStringAsync(cacheKey, content, StaticCacheOptions, token);
        }

        var builder = new StringBuilder(content, content.Length + 200);

        var acceptLanguage = context.Request.Headers.AcceptLanguage;
        builder.Replace("%lang%", ExtractLanguage(acceptLanguage));

        // ReSharper disable once StringLiteralTypo
        const string charSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        Span<char> nonce = stackalloc char[12];
        RandomNumberGenerator.GetItems(charSet, nonce);

        context.Response.Headers.ContentSecurityPolicy = GetContentSecurityPolicy(nonce);
        context.Response.Headers.CacheControl = NoCacheHeaderValue;
        builder.Replace("%nonce%", nonce);

        return Results.Text(builder.ToString(), MediaTypeNames.Text.Html);
    }
}
