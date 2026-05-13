using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IOptions<SyncOptions> _syncOptions;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<SyncOptions> syncOptions,
        IJwtIssuer jwtIssuer,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _syncOptions = syncOptions;
        _jwtIssuer = jwtIssuer;
        _logger = logger;
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
        var (accessToken, refreshToken, userId, email) = await _authService.ExchangeCodeForTokenAsync(code, callbackUrl);

        if (!string.IsNullOrEmpty(refreshToken) && !string.IsNullOrEmpty(userId))
            await _tokenStore.SaveRefreshTokenAsync(refreshToken, userId);

        if (string.IsNullOrEmpty(userId))
            return BadRequest("Authentication failed: user identity could not be determined.");

        var (apiToken, _) = _jwtIssuer.Issue(userId, email);

        // Propagate userId into the ExecutionContext so ICurrentUserService can
        // resolve it without an active HttpContext during the sync.
        BackgroundUserContext.Current = userId;
        try
        {
            await SyncCalendarEventsAsync(userId, accessToken);

            // Register webhook channels for push notifications (non-blocking)
            if (_syncOptions.Value.WebhookRegistrationEnabled)
            {
                try
                {
                    using var webhookScope = _scopeFactory.CreateScope();
                    var webhookService = webhookScope.ServiceProvider.GetRequiredService<IWebhookRegistrationService>();
                    await webhookService.RegisterAllAsync(userId, ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook registration failed during login for user {UserId}.", userId);
                }
            }
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
