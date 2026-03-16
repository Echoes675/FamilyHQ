using FamilyHQ.Services.Auth;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Services;
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
    private readonly IConfiguration _configuration;

    public AuthController(
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _authService = authService;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Initiates the OAuth2 authorization code flow by redirecting to the consent screen.
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/callback";
        var url = _authService.GetAuthorizationUrl(callbackUrl);
        return Redirect(url);
    }

    /// <summary>
    /// Receives the OAuth2 authorization code, exchanges it for tokens, issues a local JWT,
    /// and redirects the browser to the frontend /login-success page.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code)
    {
        var frontendBaseUrl = _configuration["FrontendBaseUrl"]
            ?? throw new InvalidOperationException("FrontendBaseUrl is not configured.");

        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/callback";
        var (accessToken, refreshToken, userId) = await _authService.ExchangeCodeForTokenAsync(code, callbackUrl);

        if (!string.IsNullOrEmpty(refreshToken) && !string.IsNullOrEmpty(userId))
            await _tokenStore.SaveRefreshTokenAsync(refreshToken, userId);

        if (string.IsNullOrEmpty(userId))
            return BadRequest("Authentication failed: user identity could not be determined.");

        var apiToken = GenerateJwt(userId);

        // Propagate userId into the ExecutionContext so ICurrentUserService can
        // resolve it without an active HttpContext during the sync.
        BackgroundUserContext.Current = userId;
        try
        {
            await SyncCalendarEventsAsync(userId, accessToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sync Error during login: {ex.Message}");
        }
        finally
        {
            BackgroundUserContext.Current = null;
        }

        return Redirect($"{frontendBaseUrl}/login-success?token={Uri.EscapeDataString(apiToken)}");
    }

    private string GenerateJwt(string userId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, userId)
        };
        
        var jwtKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");
        
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "FamilyHQ",
            audience: "FamilyHQ",
            claims: claims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task SyncCalendarEventsAsync(string userId, string accessToken)
    {
        using var scope = _scopeFactory.CreateScope();

        // Provide the fresh access token so the sync can use it directly
        // instead of going through the refresh token flow
        var tokenProvider = scope.ServiceProvider.GetRequiredService<IAccessTokenProvider>();
        tokenProvider.AccessToken = accessToken;

        var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();

        try
        {
            // Sync window: -30 to +365 days
            await syncService.SyncAllAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(365), CancellationToken.None);

            // Notify connected Blazor clients to refresh the UI
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>>();
            await hubContext.Clients.All.SendAsync("EventsUpdated");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sync Error: {ex.Message}");
        }
    }
}
