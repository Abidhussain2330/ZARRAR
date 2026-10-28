using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using BURAQ.Hubs.Clients;
using BURAQ.Repositories.Interface;
using Microsoft.AspNetCore.SignalR;

namespace BURAQ.Hubs;

/// <summary>
/// ZARRAR showdown channel.
/// </summary>
/// <remarks>
/// Monitors and admins join <c>ZarrarStage_{game}</c> and receive every stage event.
/// Players join <c>ZarrarPlayers_{game}</c> and only ever receive the duel state (pause,
/// resume, end) — never the opponent's progress.
/// </remarks>
[ExcludeFromCodeCoverage]
public class ZarrarHub : Hub<IZarrarClient>
{
    private const string ScreenItem = "ZarrarScreenGame";

    /// <summary>Open projector stages per game (connection IDs), for the admin's checklist.</summary>
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> Screens = new();

    internal static string StageGroup(int gameId) => $"ZarrarStage_{gameId}";

    /// <summary>How many monitor stages (not admin previews) are open on the game.</summary>
    internal static int ScreenCount(int gameId) => Screens.TryGetValue(gameId, out var set) ? set.Count : 0;

    internal static string PlayerGroup(int gameId) => $"ZarrarPlayers_{gameId}";

    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();

        if (context is null
            || !context.Request.Query.TryGetValue("game", out var gameId)
            || !int.TryParse(gameId, out var gId))
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

        if (await ContextHelper.HasMonitor(context))
        {
            await base.OnConnectedAsync();
            await Groups.AddToGroupAsync(Context.ConnectionId, StageGroup(gId));

            // The projector stage connects with screen=1; the admin's embedded preview does not.
            if (context.Request.Query.TryGetValue("screen", out var screen) && screen == "1")
            {
                Screens.GetOrAdd(gId, _ => new ConcurrentDictionary<string, byte>())[Context.ConnectionId] = 0;
                Context.Items[ScreenItem] = gId;
            }

            return;
        }

        // Players: a signed-in, non-banned user with an accepted place in this game.
        if (Context.User?.Identity?.IsAuthenticated == true
            && await ContextHelper.HasUser(context)
            && Guid.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var participationRepository = context.RequestServices.GetRequiredService<IParticipationRepository>();
            var part = await participationRepository.GetParticipation(userId, gId, Context.ConnectionAborted);

            if (part is not null && part.Status == ParticipationStatus.Accepted)
            {
                await base.OnConnectedAsync();
                await Groups.AddToGroupAsync(Context.ConnectionId, PlayerGroup(gId));
                return;
            }
        }

        Context.Abort();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(ScreenItem, out var value) && value is int gameId
            && Screens.TryGetValue(gameId, out var set))
            set.TryRemove(Context.ConnectionId, out _);

        return base.OnDisconnectedAsync(exception);
    }
}
