using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Hubs;

public class SignalRThemeBroadcaster(IHubContext<CalendarHub> hubContext) : IThemeBroadcaster
{
    // Clients.All is correct here precisely because the signal carries no period: every kiosk is
    // told "re-read yours", and each one gets its own answer. Targeting a single kiosk would need
    // an authenticated hub connection, which CalendarHub does not have.
    public Task BroadcastThemeChangedAsync(CancellationToken cancellationToken = default) =>
        hubContext.Clients.All.SendAsync("ThemeChanged", cancellationToken);
}
