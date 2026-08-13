using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Configuration;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

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
    private readonly ILogger<AuthController> _logger;
    private readonly IDataProtector _stateProtector;
    private readonly IMemoryCache _cache;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSessionOptions _jwtSessionOptions;
    private readonly TimeProvider _timeProvider;

    internal const string MissingCalendarScopeMessage =
        "Google did not grant calendar access — reconnect and allow the calendar permission.";

    internal const string MissingRefreshTokenMessage =
        "Google did not return a refresh token — reconnect to restore calendar sync.";

    public AuthController(
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<SyncOptions> syncOptions,
        ILogger<AuthController> logger,
        IDataProtectionProvider dataProtectionProvider,
        IMemoryCache cache,
        IJwtTokenService jwtTokenService,
        JwtSessionOptions jwtSessionOptions,
        TimeProvider timeProvider)
    {
        _authService = authService;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _syncOptions = syncOptions;
        _logger = logger;
        _stateProtector = dataProtectionProvider.CreateProtector("FamilyHQ.OAuthState");
        _cache = cache;
        _jwtTokenService = jwtTokenService;
        _jwtSessionOptions = jwtSessionOptions;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Initiates the OAuth2 authorization code flow by redirecting to the consent screen.
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/callback";
        var rawState = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protectedState = _stateProtector.Protect(rawState);
        Response.Cookies.Append("oauth_state", protectedState, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(10),
            Path = "/"
        });
        var url = _authService.GetAuthorizationUrl(callbackUrl, rawState);
        return Redirect(url);
    }

    /// <summary>
    /// Receives the OAuth2 authorization code, exchanges it for tokens, issues a local JWT,
    /// and redirects the browser to the frontend /login-success page.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state)
    {
        var stateCookie = Request.Cookies["oauth_state"];
        if (string.IsNullOrEmpty(stateCookie))
        {
            _logger.LogWarning("OAuth callback — state cookie missing.");
            return BadRequest("Authentication failed: invalid state.");
        }
        if (string.IsNullOrEmpty(state))
        {
            _logger.LogWarning("OAuth callback — state parameter missing.");
            return BadRequest("Authentication failed: invalid state.");
        }
        string expectedState;
        try
        {
            expectedState = _stateProtector.Unprotect(stateCookie);
        }
        catch (Exception)
        {
            _logger.LogWarning("OAuth callback — state cookie unprotect failed.");
            return BadRequest("Authentication failed: invalid state.");
        }
        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            _logger.LogWarning("OAuth callback — state mismatch.");
            return BadRequest("Authentication failed: invalid state.");
        }
        Response.Cookies.Delete("oauth_state");

        var frontendBaseUrl = _configuration["FrontendBaseUrl"]
            ?? throw new InvalidOperationException("FrontendBaseUrl is not configured.");

        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/callback";

        string? refreshToken, userId, email, grantedScope;
        try
        {
            (_, refreshToken, userId, email, grantedScope) = await _authService.ExchangeCodeForTokenAsync(code, callbackUrl);
        }
        catch (IdTokenValidationException ex)
        {
            _logger.LogWarning("id_token validation failed during OAuth callback: {Reason}", ex.Message);
            return Unauthorized("Authentication failed: id_token validation failed.");
        }

        if (string.IsNullOrEmpty(userId))
            return BadRequest("Authentication failed: user identity could not be determined.");

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _tokenStore.SaveRefreshTokenAsync(refreshToken, userId);
        }
        // FHQ-87: Google omitted the refresh token (not contractual, even with prompt=consent) and
        // nothing is stored to fall back on. Issuing the JWT here would mint an apparently-
        // authenticated session whose first sync fails with "No refresh token available" — and with
        // no token row to flag, no re-auth banner would ever surface. Mark best-effort (persists in
        // the row-exists-but-unreadable case; no-ops when no row exists) and bounce straight back
        // through Login, which always forces prompt=consent, so the next approval returns a refresh
        // token. Each pass is user-gated by the consent screen, so this cannot tight-loop.
        else if (string.IsNullOrEmpty(await _tokenStore.GetRefreshTokenAsync(userId, CancellationToken.None)))
        {
            _logger.LogWarning(
                "Login for user {UserId} returned no refresh token and none is stored; redirecting to re-consent.",
                userId);
            await _tokenStore.MarkNeedsReauthAsync(userId, MissingRefreshTokenMessage, CancellationToken.None);
            return RedirectToAction(nameof(Login));
        }

        var apiToken = _jwtTokenService.GenerateToken(userId, email);

        // FHQ-60: Google granted identity but not the calendar scope — saving + syncing would only
        // 403. Flag the account with a specific, actionable reason (surfaces in the re-auth banner
        // AND the diagnostics tab via connection-status) instead of failing silently, and skip the
        // doomed initial sync/webhook registration.
        if (!GoogleScopes.GrantsCalendar(grantedScope))
        {
            await _tokenStore.MarkNeedsReauthAsync(userId, MissingCalendarScopeMessage);
            _logger.LogWarning("Login for user {UserId} returned a grant without calendar access; flagged for re-auth.", userId);
            var missingCalendarCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _cache.Set(missingCalendarCode, apiToken, TimeSpan.FromSeconds(60));
            return Redirect($"{frontendBaseUrl}/login-success?code={Uri.EscapeDataString(missingCalendarCode)}");
        }

        // Propagate userId into the ExecutionContext so ICurrentUserService can
        // resolve it without an active HttpContext during the sync.
        BackgroundUserContext.Current = userId;
        try
        {
            await SyncCalendarEventsAsync(userId);

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
            _logger.LogError(ex, "[FHQ-46] Login sync/webhook block failed for user {UserId}: {Message}", userId, ex.Message);
        }
        finally
        {
            BackgroundUserContext.Current = null;
        }

        var successCode = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _cache.Set(successCode, apiToken, TimeSpan.FromSeconds(60));
        return Redirect($"{frontendBaseUrl}/login-success?code={Uri.EscapeDataString(successCode)}");
    }

    [HttpPost("exchange")]
    [AllowAnonymous]
    public IActionResult Exchange([FromBody] ExchangeCodeRequest request)
    {
        if (string.IsNullOrEmpty(request?.Code))
            return BadRequest();

        if (!_cache.TryGetValue(request.Code, out string? jwt))
            return BadRequest();

        _cache.Remove(request.Code);
        return Ok(new { token = jwt });
    }

    /// <summary>
    /// Re-mints the API JWT for the currently-authenticated principal (FHQ-126). Renewal only
    /// extends a LIVE session: [Authorize] requires a valid, unexpired bearer token, so an
    /// expired or missing token can never be renewed here. Total session age is capped via the
    /// auth_time claim (JwtSessionOptions) so a leaked token cannot be renewed forever. The new
    /// token is returned in the response body — never in a URL.
    /// </summary>
    [HttpPost("renew-jwt")]
    [Authorize]
    public IActionResult RenewJwt()
    {
        // MapInboundClaims=false (Program.cs) keeps the raw "sub"/"name" claim names.
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JWT renewal rejected — authenticated principal has no sub claim.");
            return Unauthorized();
        }

        var now = _timeProvider.GetUtcNow();
        var authTime = TryParseUnixSeconds(User.FindFirstValue(JwtRegisteredClaimNames.AuthTime));
        if (authTime is null)
        {
            // Grandfathering: tokens minted before auth_time existed (or with an unreadable
            // claim) start a FRESH cap window at "now". Deliberate choice for already-deployed
            // kiosks — they cannot re-authenticate silently, and every token minted from here
            // on carries auth_time, so the cap applies from this renewal onward.
            _logger.LogInformation(
                "JWT renewal grandfathered a fresh session-cap window for user {UserId} (token had no readable auth_time).",
                userId);
            authTime = now;
        }
        else if (now - authTime.Value > TimeSpan.FromDays(_jwtSessionOptions.MaxSessionAgeDays))
        {
            _logger.LogInformation(
                "JWT renewal rejected for user {UserId} — session age {SessionAgeDays:F0} days exceeds the {MaxSessionAgeDays}-day cap; re-authentication required.",
                userId, (now - authTime.Value).TotalDays, _jwtSessionOptions.MaxSessionAgeDays);
            return Unauthorized();
        }

        var email = User.FindFirstValue(JwtRegisteredClaimNames.Name);
        var token = _jwtTokenService.GenerateToken(userId, email, authTime);
        _logger.LogInformation("JWT renewed for user {UserId}.", userId);

        // OAuth BCP defence-in-depth: token responses must never be cached.
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token });
    }

    private static DateTimeOffset? TryParseUnixSeconds(string? value)
    {
        if (!long.TryParse(value, out var seconds))
            return null;
        if (seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds() || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    private async Task SyncCalendarEventsAsync(string userId)
    {
        using var scope = _scopeFactory.CreateScope();

        // FHQ-46: ENQUEUE the initial sync onto the durable queue (FHQ-37) rather than running it
        // inline. Running it inline meant a transient failure (Google/Simulator hiccup under load)
        // was silently swallowed, leaving the user's seeded events unsynced — the root cause of the
        // intermittent MonthAgendaView E2E flakes. The single-consumer worker drains the job with
        // retry/backoff (resilient to transient failures) and broadcasts EventsUpdated when done;
        // it authenticates via the stored refresh token (saved earlier in the callback).
        //
        // We then wait (bounded) for the job to drain before returning, because the caller registers
        // webhooks next and RegisterAllAsync reads the user's calendars from the local DB — which
        // only exist once the worker has synced. Login latency is unchanged (the old inline sync
        // blocked too); the gain is that the sync now self-heals transient failures via worker retry
        // rather than swallowing them, and the E2E sync-settle barrier can observe queue depth.
        try
        {
            var queue = scope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();
            var signal = scope.ServiceProvider.GetRequiredService<ISyncJobSignal>();
            await queue.EnqueueAsync(userId, null, SyncJobSource.Login, null, CancellationToken.None);
            signal.Release();

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (await queue.GetActiveJobCountAsync(userId, CancellationToken.None) > 0)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    _logger.LogWarning("[FHQ-46] Initial login sync for user {UserId} did not drain within 60s; proceeding.", userId);
                    break;
                }
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FHQ-46] Failed to enqueue/await initial calendar sync on login for user {UserId}: {Message}", userId, ex.Message);
        }
    }
}
