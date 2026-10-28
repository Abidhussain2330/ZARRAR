using System.Diagnostics.CodeAnalysis;
using BURAQ.Hubs.Clients;
using Microsoft.AspNetCore.SignalR;

namespace BURAQ.Hubs;

[ExcludeFromCodeCoverage]
public class AdminHub : Hub<IAdminClient>
{
    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();

        if (context is null || !await ContextHelper.HasAdmin(context))
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
