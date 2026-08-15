using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Registers the FHQ-101 per-endpoint rate limiter: four named fixed-window policies applied via
/// <c>[EnableRateLimiting]</c> on the auth, sync-trigger, weather-refresh and webhook actions.
/// There is deliberately NO global limiter — the kiosk polls other endpoints continuously and the
/// SignalR hub must never be limited (reconnect storms after an outage are legitimate). Fixed
/// windows are used everywhere: they are the cheapest limiter, and the worst-case boundary burst
/// (2x limit across a window edge) is already covered by the >=5x headroom in the defaults.
/// Rejected requests get a 429 with a Retry-After header (from lease metadata, falling back to
/// the policy's configured window) and an RFC7807 problem body. <c>UseRateLimiter</c> must run
/// AFTER <c>UseAuthentication</c> (per-user partitioning reads the JWT sub claim) and after
/// <c>UseForwardedHeaders</c> (per-IP partitioning needs the real client IP).
/// </summary>
public static class RateLimitingConfiguration
{
    /// <summary>Shared partition for requests whose transport exposes no remote IP.</summary>
    internal const string UnknownIpPartitionKey = "ip:unknown";

    private const string LoggerCategory = "FamilyHQ.WebApi.RateLimiting";

    public static IServiceCollection AddFamilyHqRateLimiting(
        this IServiceCollection services, RateLimitingOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, cancellationToken) =>
                HandleRejectionAsync(context, options, cancellationToken);

            limiter.AddPolicy(RateLimitPolicies.AuthPerIp, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveIpPartitionKey(httpContext),
                    _ => CreateFixedWindowOptions(options.AuthPerIp)));

            limiter.AddPolicy(RateLimitPolicies.WebhookPerIp, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveIpPartitionKey(httpContext),
                    _ => CreateFixedWindowOptions(options.WebhookPerIp)));

            limiter.AddPolicy(RateLimitPolicies.SyncTriggerPerUser, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveUserPartitionKey(httpContext),
                    _ => CreateFixedWindowOptions(options.SyncTriggerPerUser)));

            limiter.AddPolicy(RateLimitPolicies.WeatherRefreshPerUser, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveUserPartitionKey(httpContext),
                    _ => CreateFixedWindowOptions(options.WeatherRefreshPerUser)));
        });

        return services;
    }

    /// <summary>
    /// Partition key for per-IP policies. With ReverseProxy:Enabled, UseForwardedHeaders has
    /// already rewritten RemoteIpAddress to the forwarded client IP before the limiter runs.
    /// A null RemoteIpAddress (in-process/test transports) maps to one shared partition rather
    /// than escaping limiting entirely.
    /// </summary>
    internal static string ResolveIpPartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress is { } ip
            ? $"ip:{ip}"
            : UnknownIpPartitionKey;

    /// <summary>
    /// Partition key for per-user policies: the JWT sub claim (MapInboundClaims=false keeps the
    /// raw name — mirrors CurrentUserService). Unauthenticated hits would 401 anyway, but the
    /// limiter runs regardless of auth outcome, so they explicitly fall back to the IP partition.
    /// </summary>
    internal static string ResolveUserPartitionKey(HttpContext httpContext)
    {
        var sub = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return string.IsNullOrEmpty(sub)
            ? ResolveIpPartitionKey(httpContext)
            : $"user:{sub}";
    }

    /// <summary>
    /// Seconds for the Retry-After header: the lease's RetryAfter metadata (time until the fixed
    /// window resets) when present, otherwise the policy's full window. Never less than 1s — a
    /// zero would invite an immediate client retry loop.
    /// </summary>
    internal static int DeriveRetryAfterSeconds(RateLimitLease lease, TimeSpan fallbackWindow)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata) && metadata > TimeSpan.Zero
            ? metadata
            : fallbackWindow;
        return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    }

    /// <summary>Configured window for a named policy; one minute for an unknown/absent name.</summary>
    internal static TimeSpan ResolveWindowForPolicy(string? policyName, RateLimitingOptions options) =>
        policyName switch
        {
            RateLimitPolicies.AuthPerIp => options.AuthPerIp.Window,
            RateLimitPolicies.WebhookPerIp => options.WebhookPerIp.Window,
            RateLimitPolicies.SyncTriggerPerUser => options.SyncTriggerPerUser.Window,
            RateLimitPolicies.WeatherRefreshPerUser => options.WeatherRefreshPerUser.Window,
            _ => TimeSpan.FromMinutes(1)
        };

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(RateLimitPolicyOptions policy) =>
        new()
        {
            PermitLimit = policy.PermitLimit,
            Window = policy.Window,
            QueueLimit = 0
        };

    private static ValueTask HandleRejectionAsync(
        OnRejectedContext context, RateLimitingOptions options, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var policyName = httpContext.GetEndpoint()?.Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var retryAfterSeconds = DeriveRetryAfterSeconds(
            context.Lease, ResolveWindowForPolicy(policyName, options));

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // Partition keys are safe to log: IPs are operational data and the sub claim is a stable
        // identifier — never user display names or emails (logging skill).
        var partitionKey = policyName is RateLimitPolicies.SyncTriggerPerUser or RateLimitPolicies.WeatherRefreshPerUser
            ? ResolveUserPartitionKey(httpContext)
            : ResolveIpPartitionKey(httpContext);

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory)
            .LogWarning(
                "Rate limit exceeded for policy {PolicyName} on {Method} {Path} (partition {PartitionKey}); Retry-After {RetryAfterSeconds}s.",
                policyName, httpContext.Request.Method, httpContext.Request.Path, partitionKey, retryAfterSeconds);

        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "Rate limit exceeded for this endpoint. Retry after the indicated delay.",
                Type = "https://tools.ietf.org/html/rfc6585#section-4"
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken));
    }
}
