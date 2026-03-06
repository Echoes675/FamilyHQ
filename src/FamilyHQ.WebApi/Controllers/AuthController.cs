using FamilyHQ.Services.Auth;
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly IServiceScopeFactory _scopeFactory;

    public AuthController(GoogleAuthService authService, ITokenStore tokenStore, IServiceScopeFactory scopeFactory)
    {
        _authService = authService;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login()
    {
        // For MVF integration with Simulator, we immediately exchange a dummy authorize code
        var (access, refresh) = await _authService.ExchangeCodeForTokenAsync("dummy_code_123", "https://localhost/callback");
        
        if (!string.IsNullOrEmpty(refresh))
        {
            await _tokenStore.SaveRefreshTokenAsync(refresh);
        }

        // Trigger an immediate background sync so the UI updates via SignalR
        _ = Task.Run(async () => 
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
            
            try 
            {
                // Sync window: -30 to +365 days
                await syncService.SyncAllAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(365), CancellationToken.None);
                
                // Notify connected Blazor clients to refresh the UI
                var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>>();
                await hubContext.Clients.All.SendAsync("EventsUpdated");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Mock Sync Error: {ex.Message}");
            }
        });

        return Ok(new { Message = "Successfully authenticated. Refresh token saved to store." });
    }
}
