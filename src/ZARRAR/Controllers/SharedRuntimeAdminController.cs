using System.Net.Mime;
using ZARRAR.Middlewares;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Services.SharedGateway;
using ZARRAR.Services.Soc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Controllers;

/// <summary>Administrator lifecycle, capacity and health controls for shared challenge runtimes.</summary>
[RequireAdmin]
[ApiController]
[Route("api/admin/shared-runtimes")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SharedRuntimeAdminController(
    AppDbContext db,
    SharedGatewayOriginPolicy origins,
    IHttpClientFactory httpClientFactory,
    ISocEventPublisher socEvents) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SharedRuntimeStatusModel[]>> List(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.SharedRuntimeConfigurations.AsNoTracking()
            .OrderBy(x => x.ChallengeId)
            .Select(x => new SharedRuntimeStatusModel(
                x.ChallengeId,
                x.Challenge.Title,
                x.Origin,
                x.Audience,
                x.HealthPath,
                x.StateMode,
                x.IsEnabled,
                x.Sessions.Count(s => s.RevokedAtUtc == null && s.ExpiresAtUtc > now),
                x.MaxConcurrentSessions,
                x.HealthStatus,
                x.LastHealthCheckUtc,
                x.LastHealthError,
                x.UpdatedAtUtc))
            .ToArrayAsync(token);
    }

    [HttpPut("{challengeId:int}")]
    public async Task<ActionResult<SharedRuntimeStatusModel>> Upsert(int challengeId,
        SharedRuntimeUpdateModel model, CancellationToken token)
    {
        var challenge = await db.GameChallenges.SingleOrDefaultAsync(x => x.Id == challengeId, token);
        if (challenge is null)
            return NotFound(new RequestResponse("Challenge not found.", StatusCodes.Status404NotFound));
        if (!origins.TryValidate(model.Origin, out var origin, out var error))
            return BadRequest(new RequestResponse(error!, StatusCodes.Status400BadRequest));
        if (!SharedRuntimeHealthUri.TryCreate(origin!, model.HealthPath, out _, out error))
            return BadRequest(new RequestResponse(error!, StatusCodes.Status400BadRequest));

        var runtime = await db.SharedRuntimeConfigurations
            .SingleOrDefaultAsync(x => x.ChallengeId == challengeId, token);
        var created = runtime is null;
        runtime ??= new SharedRuntimeConfiguration
        {
            ChallengeId = challengeId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        if (created)
            db.SharedRuntimeConfigurations.Add(runtime);

        runtime.Origin = origin!.AbsoluteUri;
        runtime.Audience = model.Audience;
        runtime.HealthPath = model.HealthPath;
        runtime.StateMode = model.StateMode;
        runtime.MaxConcurrentSessions = model.MaxConcurrentSessions;
        runtime.IsEnabled = model.IsEnabled;
        runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
        socEvents.Enqueue(new SocEventMessage(
            EventType: created ? "SharedRuntimeConfigured" : "SharedRuntimeUpdated",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info,
            Details: $"Shared runtime configuration {(created ? "created" : "updated")} for challenge {challengeId}.",
            Source: nameof(SharedRuntimeAdminController),
            GameId: challenge.GameId,
            ChallengeId: challengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                { runtime.Audience, runtime.StateMode, runtime.MaxConcurrentSessions, runtime.IsEnabled })));
        await db.SaveChangesAsync(token);

        var active = await db.SharedRuntimeSessions.CountAsync(
            x => x.RuntimeConfigurationId == runtime.Id &&
                 x.RevokedAtUtc == null &&
                 x.ExpiresAtUtc > DateTimeOffset.UtcNow,
            token);
        return Ok(ToStatus(runtime, challenge.Title, active));
    }

    [HttpPost("{challengeId:int}/health")]
    public async Task<ActionResult<SharedRuntimeStatusModel>> CheckHealth(int challengeId, CancellationToken token)
    {
        var runtime = await db.SharedRuntimeConfigurations
            .Include(x => x.Challenge)
            .Include(x => x.Sessions)
            .SingleOrDefaultAsync(x => x.ChallengeId == challengeId, token);
        if (runtime is null)
            return NotFound();

        runtime.LastHealthCheckUtc = DateTimeOffset.UtcNow;
        if (!origins.TryValidate(runtime.Origin, out var origin, out var policyError))
        {
            runtime.HealthStatus = "Misconfigured";
            runtime.LastHealthError = policyError;
        }
        else if (!SharedRuntimeHealthUri.TryCreate(origin!, runtime.HealthPath, out var healthUri, out var healthError))
        {
            runtime.HealthStatus = "Misconfigured";
            runtime.LastHealthError = healthError;
        }
        else
        {
            try
            {
                var client = httpClientFactory.CreateClient("shared-gateway");
                using var request = new HttpRequestMessage(HttpMethod.Head, healthUri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                runtime.HealthStatus = response.IsSuccessStatusCode ? "Healthy" : "Degraded";
                runtime.LastHealthError = response.IsSuccessStatusCode
                    ? null
                    : $"Health endpoint returned HTTP {(int)response.StatusCode}.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                runtime.HealthStatus = "Unavailable";
                runtime.LastHealthError = Limit(ex.GetBaseException().Message, 1000);
            }
        }

        runtime.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var severity = runtime.HealthStatus == "Healthy" ? SocSeverity.Info : SocSeverity.High;
        socEvents.Enqueue(new SocEventMessage(
            EventType: "SharedRuntimeHealthChecked",
            Category: SocCategory.Infrastructure,
            Severity: severity,
            Details: $"Shared runtime health is {runtime.HealthStatus} for challenge {challengeId}.",
            Source: nameof(SharedRuntimeAdminController),
            GameId: runtime.Challenge.GameId,
            ChallengeId: challengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { runtime.HealthStatus })));
        await db.SaveChangesAsync(token);
        var active = runtime.Sessions.Count(x => x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTimeOffset.UtcNow);
        return Ok(ToStatus(runtime, runtime.Challenge.Title, active));
    }

    [HttpPost("{challengeId:int}/disable")]
    public async Task<IActionResult> Disable(int challengeId, CancellationToken token)
    {
        var runtime = await db.SharedRuntimeConfigurations
            .Include(x => x.Challenge)
            .SingleOrDefaultAsync(x => x.ChallengeId == challengeId, token);
        if (runtime is null)
            return NotFound();

        var now = DateTimeOffset.UtcNow;
        runtime.IsEnabled = false;
        runtime.UpdatedAtUtc = now;
        await db.SharedRuntimeSessions
            .Where(x => x.RuntimeConfigurationId == runtime.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAtUtc, now), token);
        socEvents.Enqueue(new SocEventMessage(
            EventType: "SharedRuntimeDisabled",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Medium,
            Details: $"Shared runtime was disabled and active sessions revoked for challenge {challengeId}.",
            Source: nameof(SharedRuntimeAdminController),
            GameId: runtime.Challenge.GameId,
            ChallengeId: challengeId));
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    private static SharedRuntimeStatusModel ToStatus(SharedRuntimeConfiguration runtime,
        string challenge, int activeSessions) => new(
        runtime.ChallengeId, challenge, runtime.Origin, runtime.Audience, runtime.HealthPath,
        runtime.StateMode, runtime.IsEnabled, activeSessions, runtime.MaxConcurrentSessions,
        runtime.HealthStatus, runtime.LastHealthCheckUtc, runtime.LastHealthError, runtime.UpdatedAtUtc);

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
