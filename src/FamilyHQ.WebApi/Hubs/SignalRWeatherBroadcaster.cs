namespace FamilyHQ.WebApi.Hubs;

using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

public class SignalRWeatherBroadcaster(IHubContext<CalendarHub> hubContext) : IWeatherBroadcaster
{
    public Task BroadcastWeatherUpdatedAsync(CancellationToken ct = default) =>
        hubContext.Clients.All.SendAsync("WeatherUpdated", ct);
}
