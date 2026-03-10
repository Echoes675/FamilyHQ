using FamilyHQ.Services.Auth;
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

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

    /// <summary>
    /// Authenticates with Google OAuth2 and initiates an initial calendar sync.
    /// </summary>
    /// <returns>A status message.</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest? req = null)
    {
        // For MVF integration with Simulator, we pass the simulated user id to the exchange request if present
        var authCode = !string.IsNullOrWhiteSpace(req?.SimulatedUserId) 
            ? $"dummy_code_for_{req.SimulatedUserId}" 
            : "dummy_code_123";

        var (access, refresh, userId) = await _authService.ExchangeCodeForTokenAsync(authCode, "https://localhost/callback");
        
        if (!string.IsNullOrEmpty(refresh))
        {
            await _tokenStore.SaveRefreshTokenAsync(refresh);
        }

        userId ??= "default_simulator_user";

        // Generate a local API token for the WebUI
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userId)
        };
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("SuperSecretDummyKeyForFamilyHqSimulatorMVF1"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "FamilyHQ",
            audience: "FamilyHQ",
            claims: claims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: creds
        );
        var apiToken = new JwtSecurityTokenHandler().WriteToken(token);

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

        return Ok(new 
        { 
            Message = "Successfully authenticated. Refresh token saved to store.",
            Token = apiToken,
            UserId = userId
        });
    }
}
