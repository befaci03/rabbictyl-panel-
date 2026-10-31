using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Rabbictyl.Web.Hubs;

/// <summary>
/// SignalR hub exposed at <c>/hubs/realtime</c> using the MessagePack protocol.
/// The browser client is expected to negotiate with MessagePack as well.
/// </summary>
[Authorize]
public sealed class RealtimeHub : Hub
{
    public Task Ping() => Clients.Caller.SendAsync("pong", DateTimeOffset.UtcNow);

    public Task SubscribeServer(Guid serverId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(serverId));

    public Task UnsubscribeServer(Guid serverId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(serverId));

    public static string GroupName(Guid serverId) => $"server:{serverId}";
}
