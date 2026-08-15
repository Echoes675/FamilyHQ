namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Per-endpoint rate-limiting configuration (FHQ-101). One fixed-window limit per named policy —
/// there is deliberately NO global limiter (the kiosk polls other endpoints continuously and the
/// SignalR hub must never be limited). Defaults are sized from the Deploy-Dev E2E suite, which is
/// the densest legitimate traffic this API sees: 161 scenarios sharing ONE host IP, run 6-way
/// parallel (<c>maxParallelThreads: 6</c> in the E2E runner config), so per-IP limits must clear
/// the parallel worst case rather than an average. Limits still cap abuse: hammering the
/// unauthenticated webhook forces full Google syncs (FHQ-81), and the auth callback performs a
/// Google token exchange per hit.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// GET /api/auth/login + GET /api/auth/callback, partitioned per client IP (shared bucket:
    /// one login flow consumes two permits). E2E parallel worst case: 6 threads x ~7 scenarios/min
    /// x up to 4 auth hits per flow (callback + up to 3 click-retry /login hits) ~= 170/min from
    /// the one CI-runner IP. 300/min clears that with ~1.8x margin over the pathological case
    /// (far more over the realistic ~40/min) while still capping OAuth hammering at 5 req/s.
    /// </summary>
    public RateLimitPolicyOptions AuthPerIp { get; set; } = new()
    {
        PermitLimit = 300,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/sync/webhook (unauthenticated Google push — the FHQ-81 attack surface),
    /// partitioned per client IP. E2E: 31 pushes per run, all from the single Simulator IP.
    /// Deliberately set ABOVE the whole run's total rather than its per-minute peak: with a
    /// 6-way parallel runner the pushes can cluster, and a rate-limited push would fail the
    /// scenario ~30s later as an unrelated "event never appeared" timeout. 60/min still caps
    /// forced-sync abuse (each push can trigger a full Google sync).
    /// </summary>
    public RateLimitPolicyOptions WebhookPerIp { get; set; } = new()
    {
        PermitLimit = 60,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/sync/trigger, partitioned per authenticated user (JWT sub). E2E: ~6 manual
    /// syncs per run, each under a different freshly-created user, so parallelism does not
    /// concentrate them — per-user peak 1/min; 10/min is 10x headroom while capping forced
    /// full Google syncs.
    /// </summary>
    public RateLimitPolicyOptions SyncTriggerPerUser { get; set; } = new()
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// POST /api/weather/refresh, partitioned per authenticated user (JWT sub). E2E: the
    /// weather-load step retries the refresh up to 3 times in ~30s per (unique) user, so
    /// parallelism does not concentrate them either — per-user peak 3/min; 15/min is 5x that
    /// while capping open-meteo fan-out abuse.
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
