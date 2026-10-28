using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BURAQ.Middlewares;

/// <summary>
/// Allows a specifically approved challenge developer, or an administrator,
/// without treating the developer account as a monitor or administrator.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireChallengeDeveloperAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var localizer = context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (context.HttpContext.User.Identity?.IsAuthenticated is not true ||
            !Guid.TryParse(id, out var userId))
        {
            context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_LoginRequired)], 401);
            return;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_LoginRequired)], 401);
            return;
        }

        if (user.Role == Role.Admin || user.IsChallengeDeveloper)
            return;

        context.Result = RequestResponse.Result(localizer[nameof(Resources.Program.Auth_AccessForbidden)], 403);
    }
}
