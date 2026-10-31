using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Serilog;

namespace ZARRAR.Middlewares;

/// <summary>
/// Authorization filter for privilege
/// </summary>
/// <param name="privilege"> The privilege required </param>
/// <param name="tokenScope">
/// The API token scope accepted by this endpoint. <see cref="TokenScope.None" /> — the default —
/// means the endpoint does not accept token authentication at all.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePrivilegeAttribute(Role privilege, TokenScope tokenScope = TokenScope.None)
    : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var logger =
            context.HttpContext.RequestServices.GetRequiredService<ILogger<RequirePrivilegeAttribute>>();
        var localizer =
            context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
        var diagnosticContext =
            context.HttpContext.RequestServices.GetRequiredService<IDiagnosticContext>();

        // An API token is not a user and therefore never inherits `privilege`. It is honoured only
        // where the endpoint opted in by naming a concrete scope, and only when the presented token
        // actually holds that scope — a valid token is no longer admin-equivalent (finding F-012).
        if (tokenScope is not TokenScope.None &&
            await ContextHelper.GetValidToken(context.HttpContext) is { } apiToken)
        {
            if (apiToken.Scope.Covers(tokenScope))
            {
                diagnosticContext.Set("ApiTokenId", apiToken.Id);
                diagnosticContext.Set("ApiTokenName", apiToken.Name);
                return;
            }

            logger.Log(
                StaticLocalizer[nameof(Resources.Program.Auth_PathAccessForbidden),
                    context.HttpContext.Request.Path], context.HttpContext,
                TaskStatus.Denied);

            context.Result = RequestResponse.Result(
                localizer[nameof(Resources.Program.ApiToken_InsufficientScope)],
                StatusCodes.Status403Forbidden);
            return;
        }

        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        var id = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        UserInfo? user = null;

        if (id is not null && context.HttpContext.User.Identity?.IsAuthenticated is true &&
            Guid.TryParse(id, out var guid))
            user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == guid);

        if (user is null)
        {
            context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized);
            return;
        }

        diagnosticContext.Set("UserId", user.Id);
        diagnosticContext.Set("UserName", user.UserName ?? "Anonymous");

        if (context.HttpContext.Connection.RemoteIpAddress is { } ip)
            diagnosticContext.Set("IP", ip);

        // S2 — account sharing: the same account active from two addresses within two minutes
        // (two people on one login, or a stolen session). Signals are throttled per account; three
        // flips inside the throttle window raise an alert.
        if (user.IP is { } lastAddress && context.HttpContext.Connection.RemoteIpAddress is { } currentAddress &&
            !lastAddress.Equals(currentAddress) && !System.Net.IPAddress.IsLoopback(currentAddress) &&
            !lastAddress.Equals(System.Net.IPAddress.Any) && !lastAddress.Equals(System.Net.IPAddress.IPv6Any) &&
            DateTimeOffset.UtcNow - user.LastVisitedUtc < TimeSpan.FromMinutes(2))
        {
            var flippedUser = user;
            context.HttpContext.RequestServices.GetService<ZARRAR.Services.Soc.SecuritySignalSink>()?.Signal(
                $"address-flip:{user.Id:N}",
                new ZARRAR.Services.Soc.SocEventMessage(
                    EventType: "AccountAddressFlip",
                    Category: SocCategory.Authentication,
                    Severity: SocSeverity.Low,
                    Details: "One account was used from two different network addresses within two minutes.",
                    Source: nameof(RequirePrivilegeAttribute),
                    UserId: user.Id,
                    PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                    {
                        previous = lastAddress.ToString(), current = currentAddress.ToString()
                    })),
                total => total < 3
                    ? null
                    : new SocAlert
                    {
                        SourceKey = $"account-sharing:{flippedUser.Id:N}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600}",
                        Event = "Account Used From Two Places",
                        Description = $"Account {flippedUser.UserName} switched network addresses {total} times while active (possible account sharing or a stolen session).",
                        Severity = SocSeverity.High,
                        Category = SocCategory.Authentication,
                        RiskImpact = 30,
                        Evidence = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            userId = flippedUser.Id, previous = lastAddress.ToString(), current = currentAddress.ToString(), total
                        })
                    });
        }

        if (DateTimeOffset.UtcNow - user.LastVisitedUtc > TimeSpan.FromSeconds(5))
        {
            user.UpdateByHttpContext(context.HttpContext);
            await dbContext.SaveChangesAsync(); // avoid to update ConcurrencyStamp
        }

        if (user.Role >= privilege)
            return;

        if (privilege > Role.User)
            logger.Log(
                StaticLocalizer[nameof(Resources.Program.Auth_PathAccessForbidden),
                    context.HttpContext.Request.Path], user,
                TaskStatus.Denied);

        context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_AccessForbidden)],
            StatusCodes.Status403Forbidden);
    }
}

/// <summary>
/// User required
/// </summary>
public class RequireUserAttribute() : RequirePrivilegeAttribute(Role.User);

/// <summary>
/// Monitor role required
/// </summary>
public class RequireMonitorAttribute() : RequirePrivilegeAttribute(Role.Monitor);

/// <summary>
/// Admin privilege required
/// </summary>
public class RequireAdminAttribute() : RequirePrivilegeAttribute(Role.Admin);

/// <summary>
/// Admin privilege required, but allow an API token that carries the given scope
/// </summary>
/// <param name="scope">
/// The capability the token must hold. It is a required argument on purpose: an endpoint cannot
/// opt into token authentication without stating what the token is allowed to do, and a token that
/// lacks the scope is rejected with 403 even though it is otherwise valid.
/// </param>
public class RequireAdminOrTokenAttribute(TokenScope scope) : RequirePrivilegeAttribute(Role.Admin, scope);
