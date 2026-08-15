namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Named rate-limiting policies (FHQ-101). Referenced by both the limiter registration
/// (<see cref="RateLimitingConfiguration"/>) and the <c>[EnableRateLimiting]</c> attributes on
/// the controller actions — no magic strings at the call sites.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>GET /api/auth/login and GET /api/auth/callback — per client IP.</summary>
    public const string AuthPerIp = "auth-per-ip";

    /// <summary>POST /api/sync/webhook (unauthenticated Google push) — per client IP.</summary>
    public const string WebhookPerIp = "webhook-per-ip";

    /// <summary>POST /api/sync/trigger — per authenticated user (JWT sub).</summary>
    public const string SyncTriggerPerUser = "sync-trigger-per-user";

    /// <summary>POST /api/weather/refresh — per authenticated user (JWT sub).</summary>
    public const string WeatherRefreshPerUser = "weather-refresh-per-user";
}
