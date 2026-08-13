using System.Net.Http.Headers;
using System.Text.Json;
using FamilyHQ.WebUi.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Sliding JWT renewal (FHQ-126). On startup and on a periodic tick (PeriodicTimer + CTS +
/// IAsyncDisposable, following the HeaderClock loop precedent) it checks the stored token's
/// remaining lifetime and renews via POST api/auth/renew-jwt while the token is still valid.
/// Uses the handler-free "Auth" HttpClient and attaches the bearer token itself, so renewal can
/// never re-enter CustomAuthorizationMessageHandler (no renew-on-renew recursion).
/// </summary>
public class JwtRenewalService : IJwtRenewalService
{
    private const string RenewEndpoint = "api/auth/renew-jwt";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IAuthTokenStore _tokenStore;
    private readonly JwtRenewalOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JwtRenewalService> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public JwtRenewalService(
        HttpClient httpClient,
        IAuthTokenStore tokenStore,
        JwtRenewalOptions options,
        TimeProvider timeProvider,
        ILogger<JwtRenewalService> logger)
    {
        options.Validate();
        _httpClient = httpClient;
        _tokenStore = tokenStore;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await CheckAndRenewAsync(ct);
        }
        catch (Exception ex)
        {
            // Catch-all by design (security review, FHQ-126): the startup check must NEVER fail
            // app boot — e.g. a JSException from localStorage. The daily loop below retries.
            _logger.LogWarning(ex, "Startup JWT renewal check failed; the daily loop will retry.");
        }
        _loop = RunLoopAsync(_cts.Token);
    }

    public async Task<bool> CheckAndRenewAsync(CancellationToken ct = default)
    {
        var token = await _tokenStore.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("No stored JWT; skipping renewal check.");
            return false;
        }

        var claims = JwtTokenDecoder.Decode(token);
        if (claims.ExpiresAtUtc is null)
        {
            _logger.LogWarning("Stored JWT has a missing or unreadable exp claim; treating it as expiring and renewing.");
            return await RenewNowAsync(ct) is not null;
        }

        var remaining = claims.ExpiresAtUtc.Value - _timeProvider.GetUtcNow();
        if (remaining >= TimeSpan.FromDays(_options.RenewalThresholdDays))
        {
            return false;
        }

        _logger.LogInformation(
            "Stored JWT has {RemainingDays:F1} days remaining (threshold {ThresholdDays}); renewing.",
            remaining.TotalDays,
            _options.RenewalThresholdDays);
        return await RenewNowAsync(ct) is not null;
    }

    public async Task<string?> RenewNowAsync(CancellationToken ct = default)
    {
        var token = await _tokenStore.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("No stored JWT; nothing to renew.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, RenewEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "JWT renewal failed with status {StatusCode}; keeping the existing token until the next check.",
                    (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<RenewJwtResponse>(json, JsonOptions);
            if (string.IsNullOrEmpty(result?.Token))
            {
                _logger.LogWarning("JWT renewal returned an empty token; keeping the existing token.");
                return null;
            }

            await _tokenStore.SetTokenAsync(result.Token);
            _logger.LogInformation("JWT renewed and stored successfully.");
            return result.Token;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Transient failure — never sign out here; the old token still works and the
            // next periodic tick retries.
            _logger.LogWarning(ex, "JWT renewal failed; keeping the existing token until the next check.");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("JWT renewal loop cancelled during dispose.");
            }
            catch (Exception ex)
            {
                // A faulted loop must never rethrow out of dispose; the failure was already
                // logged where it happened.
                _logger.LogDebug(ex, "JWT renewal loop had faulted before dispose.");
            }
        }
        _cts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_options.CheckInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await CheckAndRenewAsync(token);
                }
                catch (OperationCanceledException)
                {
                    throw; // shutdown — handled by the outer catch below
                }
                catch (Exception ex)
                {
                    // Catch-all by design (security review, FHQ-126): one bad tick (e.g. a
                    // JSException from localStorage) must not kill the renewal loop for the
                    // remaining months of kiosk uptime.
                    _logger.LogWarning(ex, "JWT renewal tick failed; will retry at the next tick.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Benign: app shutdown/dispose stops the loop.
            _logger.LogDebug("JWT renewal loop stopped.");
        }
    }

    private sealed record RenewJwtResponse(string? Token);
}
