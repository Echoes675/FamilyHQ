using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Hubs;

public class SignalRThemeBroadcaster(IHubContext<CalendarHub> hubContext) : IThemeBroadcaster
{
    public Task BroadcastThemeAsync(string period, CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("ThemeChanged", period, cancellationToken);
}
