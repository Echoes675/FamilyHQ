using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Hubs;

public class WeatherHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Client will receive the current weather state on connection
        // via the WeatherBroadcastService
        await base.OnConnectedAsync();
    }
}
