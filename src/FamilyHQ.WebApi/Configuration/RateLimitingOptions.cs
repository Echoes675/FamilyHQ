namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Per-endpoint rate-limiting configuration (FHQ-101). One fixed-window limit per named policy —
/// there is deliberately NO global limiter (the kiosk polls other endpoints continuously and the
/// SignalR hub must never be limited). Defaults are sized from observed Deploy-Dev E2E traffic
/// (161 scenarios from a single host IP) with at least 5x headroom over the observed peak while
/// still capping abuse: hammering the unauthenticated webhook forces full Google syncs (FHQ-81),
/// and the auth callback performs a Google token exchange per hit.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// GET /api/auth/login + GET /api/auth/callback, partitioned per client IP (shared bucket:
    /// one login flow consumes two permits). E2E worst case: ~181 login flows x 2 requests
    /// (+ up to 2 click-retry /login hits per flow) sequentially — observed peak &lt; 60/min;
    /// 300/min gives &gt;5x headroom yet still caps OAuth hammering at 5 req/s per IP.
    /// </summary>
    public RateLimitPolicyOptions AuthPerIp { get; set; } = new()
    {
        PermitLimit = 300,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/sync/webhook (unauthenticated Google push — the FHQ-81 attack surface),
    /// partitioned per client IP. E2E: 31 pushes per run, all sequential, observed peak ~6/min
    /// from the single Simulator IP; 30/min is 5x that while capping forced-sync abuse.
    /// </summary>
    public RateLimitPolicyOptions WebhookPerIp { get; set; } = new()
    {
        PermitLimit = 30,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/sync/trigger, partitioned per authenticated user (JWT sub). E2E: 6 manual
    /// syncs per run, each under a different freshly-created user — per-user peak 1/min;
    /// 10/min is 10x headroom while capping forced full Google syncs.
    /// </summary>
    public RateLimitPolicyOptions SyncTriggerPerUser { get; set; } = new()
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/weather/refresh, partitioned per authenticated user (JWT sub). E2E: the
    /// weather-load step retries the refresh up to 3 times in ~30s per (unique) user —
    /// per-user peak 3/min; 15/min is 5x that while capping open-meteo fan-out abuse.
    /// </summary>
    public RateLimitPolicyOptions WeatherRefreshPerUser { get; set; } = new()
    {
        PermitLimit = 15,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    public void Validate()
    {
        AuthPerIp.Validate(nameof(AuthPerIp));
        WebhookPerIp.Validate(nameof(WebhookPerIp));
        SyncTriggerPerUser.Validate(nameof(SyncTriggerPerUser));
        WeatherRefreshPerUser.Validate(nameof(WeatherRefreshPerUser));
    }
}
