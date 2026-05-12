using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Data;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Configuration;
using FamilyHQ.WebApi.Models;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string GoogleProvider = "Google";

    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IOptions<SyncOptions> _syncOptions;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly ILogger<AuthController> _logger;
    private readonly FamilyHqDbContext _dbContext;
    private readonly IOptions<IssueTokenEndpointOptions> _issueTokenOptions;

    public AuthController(
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<SyncOptions> syncOptions,
        IJwtIssuer jwtIssuer,
        ILogger<AuthController> logger,
        FamilyHqDbContext dbContext,
        IOptions<IssueTokenEndpointOptions> issueTokenOptions)
    {
        _authService = authService;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _syncOptions = syncOptions;
        _jwtIssuer = jwtIssuer;
        _logger = logger;
        _dbContext = dbContext;
        _issueTokenOptions = issueTokenOptions;
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

    /// <summary>
    /// Issues a JWT for a known user without driving the Google OAuth UI. Gated by a shared
    /// secret and only registered in non-prod environments. Intended for unattended smoke-test
    /// pipelines. See <see cref="IssueTokenEndpointOptions"/> and the FHQ-23 spec for the four
    /// guards that protect against accidental prod exposure.
    /// </summary>
    [HttpPost("issue-token")]
    public async Task<IActionResult> IssueToken([FromBody] IssueTokenRequest? request)
    {
        var options = _issueTokenOptions.Value;

        // Guard 1: feature must be opted-in. Return framework-style 404 so callers cannot
        // distinguish "disabled" from "unknown route".
        if (!options.Enabled)
        {
            return NotFound();
        }

        var callerIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Guard 2: validate the bearer secret with constant-time comparison. Mismatched or
        // missing secrets return 404 (not 401) so the endpoint exposes no enumeration signal.
        if (!TryAuthenticateCaller(options.Secret))
        {
            _logger.LogWarning(
                "auth.issue_token.unauthorised caller_ip={CallerIp}",
                callerIp);
            return NotFound();
        }

        // Validate the request body. A missing or empty userId is a client error.
        if (request is null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { error = "userId is required." });
        }

        // Lookup uses the same (UserId, Provider) pair the rest of the app keys on. Missing
        // user → 404, structurally identical to the unauthorised response.
        var exists = await _dbContext.UserTokens
            .AsNoTracking()
            .AnyAsync(t => t.UserId == request.UserId && t.Provider == GoogleProvider);
        if (!exists)
        {
            _logger.LogWarning(
                "auth.issue_token.unknown_user user_id={UserId} caller_ip={CallerIp}",
                request.UserId,
                callerIp);
            return NotFound();
        }

        // Email is not persisted, so the token only carries sub/unique_name. That's sufficient
        // for downstream authorisation which already keys off the sub claim.
        var (token, expiresAt) = _jwtIssuer.Issue(request.UserId);

        _logger.LogInformation(
            "auth.issue_token.success user_id={UserId} caller_ip={CallerIp}",
            request.UserId,
            callerIp);

        return Ok(new IssueTokenResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
        });
    }

    private bool TryAuthenticateCaller(string configuredSecret)
    {
        if (string.IsNullOrEmpty(configuredSecret))
        {
            // Refuse to authenticate against an unconfigured secret — fail closed.
            return false;
        }

        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return false;
        }

        var header = headerValues.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = header.AsSpan(prefix.Length).ToString();
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);
        return CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
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
