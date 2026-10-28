using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using BURAQ.Hubs.Clients;
using BURAQ.Repositories.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BURAQ.Hubs;

[Authorize]
[ExcludeFromCodeCoverage]
public class UserHub : Hub<IUserClient>
{
    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();
        var principal = Context.User;

        if (context is null
            || principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !context.Request.Query.TryGetValue("game", out var gameId)
            || !int.TryParse(gameId, out var gId))
        {
            Context.Abort();
            return;
        }

        var gameRepository = context.RequestServices.GetRequiredService<IGameRepository>();
        if (!await gameRepository.CanReceiveGameNoticesAsync(gId, userId, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"Game_{gId}");
    }
}
