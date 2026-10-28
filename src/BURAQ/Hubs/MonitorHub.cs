using System.Diagnostics.CodeAnalysis;
using BURAQ.Hubs.Clients;
using BURAQ.Repositories.Interface;
using Microsoft.AspNetCore.SignalR;

namespace BURAQ.Hubs;

[ExcludeFromCodeCoverage]
public class MonitorHub : Hub<IMonitorClient>
{
    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();

        // The monitor stream carries live submissions and cheat data, so a token is accepted here
        // only when it holds TokenScope.GameMonitor (finding F-012).
        if (context is null
            || !context.Request.Query.TryGetValue("game", out var gameId)
            || !int.TryParse(gameId, out var gId)
            || (!await ContextHelper.HasMonitor(context) &&
                !await ContextHelper.HasValidToken(context, TokenScope.GameMonitor)))
        {
            Context.Abort();
            return;
        }

        var gameRepository = context.RequestServices.GetRequiredService<IGameRepository>();
        if (!await gameRepository.HasGameAsync(gId))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();

        await Groups.AddToGroupAsync(Context.ConnectionId, $"Game_{gId}");
    }
}
