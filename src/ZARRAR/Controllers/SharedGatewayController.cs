using System.Net;
using System.Net.Mime;
using ZARRAR.Middlewares;
using ZARRAR.Models.Internal;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.SharedGateway;
using ZARRAR.Services.Soc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ZARRAR.Controllers;

/// <summary>Authenticated reverse proxy for stateless or team-namespaced shared challenge services.</summary>
[RequireUser]
[ApiController]
[Route("api/gateway")]
public sealed class SharedGatewayController(
    AppDbContext db,
    UserManager<UserInfo> userManager,
    IDivisionRepository divisions,
    SharedGatewayTokenService tokens,
    SharedGatewayOriginPolicy origins,
    IHttpClientFactory httpClientFactory,
    IOptions<SharedGatewayOptions> options,
    ISocEventPublisher socEvents,
    ILogger<SharedGatewayController> logger) : ControllerBase
{
    private const int SessionLockNamespace = 0x42575251;
    private static readonly HashSet<string> BlockedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Connection", "Host", "Keep-Alive", "Proxy-Authenticate",
        "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Content-Length",
        "X-Zarrar-Access-Token", "X-Zarrar-Session-Id", "X-Zarrar-Team-Id", "X-Zarrar-Participation-Id"
    };
    private static readonly HashSet<string> BlockedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer",
        "Transfer-Encoding", "Upgrade", "Set-Cookie", "Content-Length", "Location"
    };
    private static readonly HashSet<string> IdentityQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "team", "teamId", "participation", "participationId", "sessionId", "zarrarToken", "accessToken"
    };

    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    [Route("{gameId:int}/{challengeId:int}/{**path}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task Proxy(int gameId, int challengeId, string? path, CancellationToken token)
    {
        if (!tokens.IsConfigured)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!TryBuildRelativeTarget(path, out var relativeTarget))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var runtime = await db.SharedRuntimeConfigurations
            .Include(x => x.Challenge)
            .ThenInclude(x => x.Game)
            .SingleOrDefaultAsync(x => x.ChallengeId == challengeId, token);
        var now = DateTimeOffset.UtcNow;
        if (runtime is null || !runtime.IsEnabled || !runtime.Challenge.IsEnabled ||
            runtime.Challenge.GameId != gameId ||
            now < runtime.Challenge.Game.StartTimeUtc ||
            (!runtime.Challenge.Game.PracticeMode && runtime.Challenge.Game.EndTimeUtc < now))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var participation = await db.Participations
            .Include(x => x.Team)
            .SingleOrDefaultAsync(x => x.GameId == gameId &&
                x.Members.Any(m => m.UserId == user.Id && m.GameId == gameId), token);
        if (participation is null || participation.Status != ParticipationStatus.Accepted)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var permission = await divisions.GetPermission(participation.DivisionId, challengeId, token);
        if (!permission.HasFlag(GamePermission.ViewChallenge))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!origins.TryValidate(runtime.Origin, out var origin, out _))
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var requestSize = options.Value.MaxRequestBodyBytes;
        if (Request.ContentLength > requestSize)
        {
            Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        var bodyFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyFeature is { IsReadOnly: false })
            bodyFeature.MaxRequestBodySize = requestSize;

        var session = await AcquireSession(runtime, participation, now, token);
        if (session is null)
            return;

        var upstreamToken = tokens.Mint(runtime.Audience, gameId, challengeId,
            participation.Id, participation.TeamId, session.SessionId, now);
        var query = QueryString.Create(Request.Query
            .Where(x => !IdentityQueryKeys.Contains(x.Key))
            .SelectMany(x => x.Value.Select(value =>
                new KeyValuePair<string, string?>(x.Key, value))));
        var upstreamUri = new Uri(origin!, relativeTarget + query.Value);

        using var request = new HttpRequestMessage(new HttpMethod(Request.Method), upstreamUri);
        if (Request.ContentLength is > 0 || Request.Headers.ContainsKey("Transfer-Encoding"))
            request.Content = new StreamContent(Request.Body);

        foreach (var header in Request.Headers)
        {
            if (BlockedRequestHeaders.Contains(header.Key))
                continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
        request.Headers.TryAddWithoutValidation("X-Zarrar-Access-Token", upstreamToken);
        request.Headers.TryAddWithoutValidation("X-Zarrar-Session-Id", session.SessionId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Zarrar-Request-Id", HttpContext.TraceIdentifier);

        try
        {
            var client = httpClientFactory.CreateClient("shared-gateway");
            using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            Response.StatusCode = (int)upstream.StatusCode;
            CopyResponseHeaders(upstream);
            RewriteLocation(upstream, origin!, gameId, challengeId);
            Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Vary = "Cookie, Authorization";

            if (Request.Method != HttpMethods.Head && upstream.StatusCode != HttpStatusCode.NoContent)
                await upstream.Content.CopyToAsync(Response.Body, token);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) &&
                                   !token.IsCancellationRequested && !Response.HasStarted)
        {
            logger.LogWarning(ex, "Shared runtime {ChallengeId} request failed.", challengeId);
            runtime.HealthStatus = "Unavailable";
            runtime.LastHealthError = Limit(ex.GetBaseException().Message, 1000);
            runtime.LastHealthCheckUtc = DateTimeOffset.UtcNow;
            await socEvents.PublishAsync(new SocEventMessage(
                EventType: "SharedRuntimeRequestFailed",
                Category: SocCategory.Infrastructure,
                Severity: SocSeverity.High,
                Details: $"Shared runtime request failed for challenge {challengeId}.",
                Source: nameof(SharedGatewayController),
                TeamId: participation.TeamId,
                GameId: gameId,
                ChallengeId: challengeId,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                    { session.SessionId, method = Request.Method })), token);
            Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    private async Task<SharedRuntimeSession?> AcquireSession(SharedRuntimeConfiguration runtime,
        Participation participation, DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            [SessionLockNamespace, runtime.ChallengeId], cancellationToken: token);

        var session = await db.SharedRuntimeSessions.SingleOrDefaultAsync(x =>
            x.RuntimeConfigurationId == runtime.Id && x.ParticipationId == participation.Id, token);
        var startsLease = session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now;
        if (startsLease)
        {
            var active = await db.SharedRuntimeSessions.CountAsync(x =>
                x.RuntimeConfigurationId == runtime.Id && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, token);
            if (active >= runtime.MaxConcurrentSessions)
            {
                await transaction.RollbackAsync(token);
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                Response.Headers.RetryAfter = "30";
                return null;
            }

            if (session is null)
            {
                session = new SharedRuntimeSession
                {
                    RuntimeConfigurationId = runtime.Id,
                    ParticipationId = participation.Id
                };
                db.SharedRuntimeSessions.Add(session);
            }

            // A disable operation revokes every lease. Re-enabling the runtime must issue a fresh,
            // unlinkable lease instead of permanently locking all previous participants out.
            session.SessionId = Guid.NewGuid();
            session.CreatedAtUtc = now;
            session.RevokedAtUtc = null;
        }

        if (session is null)
            throw new InvalidOperationException("A shared runtime lease could not be created.");

        session.LastAccessAtUtc = now;
        session.ExpiresAtUtc = now.AddMinutes(Math.Clamp(options.Value.SessionMinutes, 1, 60));
        if (startsLease)
            socEvents.Enqueue(new SocEventMessage(
                EventType: "SharedRuntimeSessionStarted",
                Category: SocCategory.PlatformSecurity,
                Severity: SocSeverity.Info,
                Details: $"A shared runtime session started for team {participation.Team.Name}.",
                Source: nameof(SharedGatewayController),
                TeamId: participation.TeamId,
                GameId: participation.GameId,
                ChallengeId: runtime.ChallengeId,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                    { session.SessionId, runtime.StateMode, runtime.Audience })));

        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return session;
    }

    private void CopyResponseHeaders(HttpResponseMessage upstream)
    {
        foreach (var header in upstream.Headers.Concat(upstream.Content.Headers))
            if (!BlockedResponseHeaders.Contains(header.Key))
                Response.Headers.Append(header.Key, header.Value.ToArray());
    }

    private void RewriteLocation(HttpResponseMessage upstream, Uri origin, int gameId, int challengeId)
    {
        var location = upstream.Headers.Location;
        if (location is null)
            return;
        var absolute = location.IsAbsoluteUri ? location : new Uri(origin, location);
        if (absolute.GetLeftPart(UriPartial.Authority)
            .Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            Response.Headers.Location = SharedRuntimeRoutes.ForGame(gameId, challengeId).TrimEnd('/') +
                absolute.PathAndQuery;
    }

    internal static bool TryBuildRelativeTarget(string? path, out string target)
    {
        target = string.Empty;
        if (path?.Contains("..", StringComparison.Ordinal) == true ||
            path?.Contains('\\') == true ||
            path?.Any(char.IsControl) == true)
            return false;
        target = string.IsNullOrEmpty(path) ? string.Empty : path.TrimStart('/');
        return true;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
