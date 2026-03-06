using FamilyHQ.Core.DTOs;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Hubs;

public class CalendarHub : Hub
{
    // Clients connect to the Hub, and the server will call methods on them
    // like "EventsUpdated" to force them to refresh or append data.
    
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
