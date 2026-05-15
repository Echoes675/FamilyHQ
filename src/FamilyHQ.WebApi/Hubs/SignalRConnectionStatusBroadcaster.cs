using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Hubs;

public class SignalRConnectionStatusBroadcaster(IHubContext<CalendarHub> hubContext) : IConnectionStatusBroadcaster
{
    public Task BroadcastConnectionStatusUpdatedAsync(CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("ConnectionStatusUpdated", cancellationToken);
}
