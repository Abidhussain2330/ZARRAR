using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ZARRAR.Middlewares;

/// <summary>
/// The rate limiter middleware
/// </summary>
[ExcludeFromCodeCoverage]
public static class RateLimiter
{
    public enum LimitPolicy
    {
        /// <summary>
        /// Concurrency operation limit
        /// </summary>
        Concurrency,

        /// <summary>
        /// Register limit
        /// </summary>
        Register,

        /// <summary>
        /// Database query limit
        /// </summary>
        Query,

        /// <summary>
        /// Container operation limit
        /// </summary>
        Container,

        /// <summary>
        /// Flag submit limit
        /// </summary>
        Submit,

        /// <summary>
        /// Pow challenge generation limit
        /// </summary>
        PowChallenge,

        /// <summary>
        /// Credential submission limit, applied to the login endpoint
        /// </summary>
        Login
    }

    /// <summary>
    /// Build the partition key for a named policy.
    /// </summary>
    /// <remarks>
    /// Named policies must be partitioned per caller. A single shared bucket lets one client
    /// exhaust the budget for everyone, turning any rate limit into a denial-of-service tool
    /// against legitimate competitors (finding F-008). Authenticated callers are keyed by user id
    /// so that rotating source addresses does not reset the budget; anonymous callers fall back to
    /// the remote address.
    /// </remarks>
    static string GetPartitionKey(HttpContext context, string policy)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is not null)
            return $"{policy}:u:{userId}";

        var address = context.Connection.RemoteIpAddress;

        return address is null
            ? $"{policy}:anon"
            : $"{policy}:ip:{address}";
    }

    /// <summary>
    /// Sustained rate-limit hits are a flood, a scraper or a brute-force tool: tell the SOC, once
    /// per caller per few minutes (the sink folds the repeats into a count).
    /// </summary>
    static void ReportRejection(HttpContext context)
    {
        var sink = context.RequestServices.GetService<ZARRAR.Services.Soc.SecuritySignalSink>();
        if (sink is null)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var caller = userId is not null ? $"u:{userId}" : $"ip:{address}";
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Length > 200)
            path = path[..200];

        sink.Signal($"ratelimit:{caller}", new ZARRAR.Services.Soc.SocEventMessage(
            EventType: "RateLimitExceeded",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Low,
            Details: "Requests were refused by the rate limiter (flooding, scraping or automated guessing).",
            Source: nameof(RateLimiter),
            UserId: Guid.TryParse(userId, out var id) ? id : null,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                address,
                method = context.Request.Method,
                path
            })));
    }

    public static void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId is not null)
                return RateLimitPartition.GetSlidingWindowLimiter(userId,
                    _ => new()
                    {
                        QueueLimit = 60,
                        PermitLimit = 150,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        SegmentsPerWindow = 6
                    });

            var address = context.Connection.RemoteIpAddress;

            // Loopback previously received an unlimited bypass. Behind a reverse proxy that does
            // not set forwarded headers, RemoteIpAddress is 127.0.0.1 for *every* request, so that
            // bypass silently disabled rate limiting platform-wide. Loopback now gets a generous
            // but finite budget instead, which still accommodates local health checks and probes.
            if (address is null || IPAddress.IsLoopback(address))
                return RateLimitPartition.GetSlidingWindowLimiter(IPAddress.Loopback.ToString(),
                    _ => new()
                    {
                        QueueLimit = 120,
                        PermitLimit = 3000,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        SegmentsPerWindow = 6
                    });

            return RateLimitPartition.GetSlidingWindowLimiter(address.ToString(),
                _ => new()
                {
                    QueueLimit = 60,
                    PermitLimit = 150,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    SegmentsPerWindow = 6
                });
        });
        options.OnRejected = async (context, cancellationToken) =>
        {
            ReportRejection(context.HttpContext);
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = MediaTypeNames.Application.Json;

            var localizer =
                context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
            var afterSec = (int)TimeSpan.FromMinutes(1).TotalSeconds;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                afterSec = (int)retryAfter.TotalSeconds;

            context.HttpContext.Response.Headers.RetryAfter = afterSec.ToString(NumberFormatInfo.InvariantInfo);
            await context.HttpContext.Response.WriteAsJsonAsync(
                new RequestResponse(localizer[nameof(Resources.Program.RateLimit_TooManyRequests), afterSec],
                    StatusCodes.Status429TooManyRequests
                ), cancellationToken);
        };
        // Every named policy below is partitioned per caller (see GetPartitionKey). The permit
        // values are the original shared-bucket values, now applied to each caller individually.
        AddPartitionedConcurrencyLimiter(options, nameof(LimitPolicy.Concurrency), permitLimit: 1, queueLimit: 20);

        AddPartitionedSlidingWindowLimiter(options, nameof(LimitPolicy.Register), permitLimit: 20, queueLimit: 10,
            window: TimeSpan.FromSeconds(150), segmentsPerWindow: 5);

        // Credential submission. A shared school or event network can legitimately contain many
        // competitors, so allow up to 300 attempts per source IP over five minutes. The policy
        // remains finite and rejects immediately rather than queueing failed credential attempts.
        AddPartitionedSlidingWindowLimiter(options, nameof(LimitPolicy.Login), permitLimit: 300, queueLimit: 0,
            window: TimeSpan.FromMinutes(5), segmentsPerWindow: 5);

        AddPartitionedTokenBucketLimiter(options, nameof(LimitPolicy.Query), tokenLimit: 100, tokensPerPeriod: 10,
            replenishmentPeriod: TimeSpan.FromSeconds(10));

        AddPartitionedTokenBucketLimiter(options, nameof(LimitPolicy.PowChallenge), tokenLimit: 40, tokensPerPeriod: 5,
            replenishmentPeriod: TimeSpan.FromSeconds(30));

        AddPartitionedTokenBucketLimiter(options, nameof(LimitPolicy.Container), tokenLimit: 120, tokensPerPeriod: 30,
            replenishmentPeriod: TimeSpan.FromSeconds(10));

        AddPartitionedTokenBucketLimiter(options, nameof(LimitPolicy.Submit), tokenLimit: 100, tokensPerPeriod: 50,
            replenishmentPeriod: TimeSpan.FromSeconds(5));
    }

    static void AddPartitionedConcurrencyLimiter(RateLimiterOptions options, string policy, int permitLimit,
        int queueLimit) =>
        options.AddPolicy(policy, context =>
            RateLimitPartition.GetConcurrencyLimiter(GetPartitionKey(context, policy),
                _ => new() { PermitLimit = permitLimit, QueueLimit = queueLimit }));

    static void AddPartitionedSlidingWindowLimiter(RateLimiterOptions options, string policy, int permitLimit,
        int queueLimit, TimeSpan window, int segmentsPerWindow) =>
        options.AddPolicy(policy, context =>
            RateLimitPartition.GetSlidingWindowLimiter(GetPartitionKey(context, policy),
                _ => new()
                {
                    PermitLimit = permitLimit,
                    QueueLimit = queueLimit,
                    Window = window,
                    SegmentsPerWindow = segmentsPerWindow,
                    QueueProcessingOrder = QueueProcessingOrder.NewestFirst
                }));

    static void AddPartitionedTokenBucketLimiter(RateLimiterOptions options, string policy, int tokenLimit,
        int tokensPerPeriod, TimeSpan replenishmentPeriod) =>
        options.AddPolicy(policy, context =>
            RateLimitPartition.GetTokenBucketLimiter(GetPartitionKey(context, policy),
                _ => new()
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = replenishmentPeriod
                }));
}
