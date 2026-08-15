using System.Diagnostics;
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
    private const string IpPartitionKeyPrefix = "ip:";
    private const string UserPartitionKeyPrefix = "user:";

    /// <summary>Shared partition for requests whose transport exposes no remote IP.</summary>
    internal const string UnknownIpPartitionKey = IpPartitionKeyPrefix + "unknown";

    private const string LoggerCategory = "FamilyHQ.WebApi.RateLimiting";

    public static IServiceCollection AddFamilyHqRateLimiting(
        this IServiceCollection services, RateLimitingOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, cancellationToken) =>
                HandleRejectionAsync(context, options, cancellationToken);

            // Every policy is registered through the same two seams, so the policy -> limits and
            // policy -> partition mappings exist exactly once and are unit-testable on their own
            // (the framework's PolicyMap is internal, so a cross-wiring here would otherwise be
            // invisible to tests). Looping over RateLimitPolicies.All also makes it impossible to
            // add a policy name without registering it.
            foreach (var policyName in RateLimitPolicies.All)
            {
                // Resolved at registration (boot), so a policy name without matching options is a
                // startup failure rather than a silently unlimited endpoint.
                var policyOptions = ResolveOptionsForPolicy(policyName, options)
                    ?? throw new InvalidOperationException(
                        $"{nameof(RateLimitingOptions)} has no limits mapped for policy '{policyName}'.");

                limiter.AddPolicy(policyName, httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ResolvePartitionKeyForPolicy(policyName, httpContext),
                        _ => CreateFixedWindowOptions(policyOptions)));
            }
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
            ? $"{IpPartitionKeyPrefix}{ip}"
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
            : $"{UserPartitionKeyPrefix}{sub}";
    }

    /// <summary>
    /// The partition a named policy counts against: per-user policies key on the JWT sub (with the
    /// IP fallback above), everything else keys on the client IP. Single-sourced so the limiter
    /// registration and the rejection log can never disagree about which bucket was consumed.
    /// </summary>
    internal static string ResolvePartitionKeyForPolicy(string? policyName, HttpContext httpContext) =>
        policyName switch
        {
            RateLimitPolicies.SyncTriggerPerUser or RateLimitPolicies.WeatherRefreshPerUser =>
                ResolveUserPartitionKey(httpContext),
            _ => ResolveIpPartitionKey(httpContext)
        };

    /// <summary>Limits configured for a named policy; null when the name is unknown.</summary>
    internal static RateLimitPolicyOptions? ResolveOptionsForPolicy(
        string? policyName, RateLimitingOptions options) =>
        policyName switch
        {
            RateLimitPolicies.AuthPerIp => options.AuthPerIp,
            RateLimitPolicies.WebhookPerIp => options.WebhookPerIp,
            RateLimitPolicies.SyncTriggerPerUser => options.SyncTriggerPerUser,
            RateLimitPolicies.WeatherRefreshPerUser => options.WeatherRefreshPerUser,
            _ => null
        };

    /// <summary>
    /// Seconds for the Retry-After header: the lease's RetryAfter metadata (time until the fixed
    /// window resets) whenever the lease supplies it — including a zero at the window edge, where
    /// the honest answer is "immediately" rather than a full window — otherwise the policy's
    /// configured window. Never less than 1s: a zero would invite an immediate client retry loop.
    /// </summary>
    internal static int DeriveRetryAfterSeconds(RateLimitLease lease, TimeSpan fallbackWindow)
    {
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? metadata
            : fallbackWindow;
        return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    }

    /// <summary>Configured window for a named policy; one minute for an unknown/absent name.</summary>
    internal static TimeSpan ResolveWindowForPolicy(string? policyName, RateLimitingOptions options) =>
        ResolveOptionsForPolicy(policyName, options)?.Window ?? TimeSpan.FromMinutes(1);

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
        var partitionKey = ResolvePartitionKeyForPolicy(policyName, httpContext);

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory)
            .LogWarning(
                "Rate limit exceeded for policy {PolicyName} on {Method} {Path} (partition {PartitionKey}); Retry-After {RetryAfterSeconds}s.",
                policyName, httpContext.Request.Method, httpContext.Request.Path, partitionKey, retryAfterSeconds);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Rate limit exceeded for this endpoint. Retry after the indicated delay.",
            Type = "https://tools.ietf.org/html/rfc6585#section-4"
        };

        // The rejection is written directly rather than through IProblemDetailsService (no
        // exception is in flight), so carry the trace id the framework's writer would have added —
        // 429s are exactly the responses worth correlating in Seq.
        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken));
    }
}
