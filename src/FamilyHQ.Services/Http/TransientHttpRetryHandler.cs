using System.Globalization;
using System.Net;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Http;

/// <summary>
/// FHQ-114: transient-fault retry for the ip-api, Nominatim and Open-Meteo typed clients, which
/// previously had only a Timeout — so a single 429/5xx/network blip lost a whole
/// DayThemeScheduler theme-recalculation or weather-poll cycle.
/// <para>
/// Shaped as a <see cref="DelegatingHandler"/> rather than the interface decorator used for Google
/// (<c>ResilientGoogleCalendarClient</c>): those three services talk raw HTTP through an injected
/// <see cref="HttpClient"/>, so the transient signal — status code, <c>Retry-After</c>, ip-api's
/// <c>X-Ttl</c>, connection failures — lives at the message level. One handler covers all three
/// clients with no per-service decorator, and it is the only layer that still sees the response
/// headers (<c>GetFromJsonAsync</c> throws them away).
/// </para>
/// <para>
/// Only idempotent methods (GET/HEAD) are replayed: a 5xx may still have been processed upstream.
/// Sleeps happen inside <c>SendAsync</c>, so each client's Timeout is the TOTAL budget for the
/// whole sequence — see <see cref="ExternalHttpResilienceOptions.LocationTimeout"/>.
/// </para>
/// </summary>
public sealed class TransientHttpRetryHandler(
    IOptions<ExternalHttpResilienceOptions> options,
    TimeProvider timeProvider,
    ILogger<TransientHttpRetryHandler> logger) : DelegatingHandler
{
    /// <summary>ip-api.com reports the seconds left in its rate-limit window here, not in Retry-After.</summary>
    private const string RateLimitTtlHeaderName = "X-Ttl";

    private readonly ExternalHttpResilienceOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            TimeSpan delay;
            try
            {
                var response = await base.SendAsync(request, ct);

                if (!IsTransient(response.StatusCode) || !CanRetry(request, attempt))
                    return response;

                delay = ComputeRetryDelay(response, attempt);
                if (delay > _options.MaxRetryDelay)
                {
                    logger.LogWarning(
                        "{Host}{Path} asked for a {DelaySeconds}s wait after {StatusCode}; surfacing instead of holding the request open.",
                        Host(request), Path(request), (int)delay.TotalSeconds, (int)response.StatusCode);
                    return response;
                }

                logger.LogWarning(
                    "Transient {StatusCode} from {Host}{Path}; retry {Attempt}/{MaxAttempts} after {DelayMs}ms.",
                    (int)response.StatusCode, Host(request), Path(request), attempt, _options.MaxAttempts,
                    (int)delay.TotalMilliseconds);

                response.Dispose(); // release the connection before sleeping
            }
            catch (HttpRequestException ex) when (CanRetry(request, attempt))
            {
                delay = ComputeExponentialDelay(attempt);
                logger.LogWarning(ex,
                    "Transient network failure calling {Host}{Path}; retry {Attempt}/{MaxAttempts} after {DelayMs}ms.",
                    Host(request), Path(request), attempt, _options.MaxAttempts, (int)delay.TotalMilliseconds);
            }

            await Task.Delay(delay, timeProvider, ct);
        }
    }

    /// <summary>
    /// Delay before the next attempt: the server's own hint when it gave one (<c>Retry-After</c>
    /// delta or HTTP-date, then ip-api's <c>X-Ttl</c>), otherwise exponential backoff with full
    /// jitter. May exceed <see cref="ExternalHttpResilienceOptions.MaxRetryDelay"/> — the caller
    /// decides to surface rather than sleep in that case.
    /// </summary>
    internal TimeSpan ComputeRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retryAfter?.Date is { } date)
        {
            var untilDate = date - timeProvider.GetUtcNow();
            if (untilDate > TimeSpan.Zero)
                return untilDate;
        }

        if (response.Headers.TryGetValues(RateLimitTtlHeaderName, out var ttlValues)
            && int.TryParse(ttlValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttlSeconds)
            && ttlSeconds > 0)
            return TimeSpan.FromSeconds(ttlSeconds);

        return ComputeExponentialDelay(attempt);
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || ((int)status >= 500 && status != HttpStatusCode.NotImplemented);

    private bool CanRetry(HttpRequestMessage request, int attempt)
        => attempt < _options.MaxAttempts && IsIdempotent(request.Method);

    // A replayed POST/PUT/DELETE could duplicate an upstream side effect; all three clients are GETs.
    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    private TimeSpan ComputeExponentialDelay(int attempt)
    {
        var expMs = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMs = expMs * Random.Shared.NextDouble(); // full jitter: [exp, 2*exp)
        return TimeSpan.FromMilliseconds(expMs + jitterMs);
    }

    // Host + path only: never the query string, which carries the family's latitude/longitude.
    private static string Host(HttpRequestMessage request) => request.RequestUri?.Host ?? "unknown";

    private static string Path(HttpRequestMessage request) => request.RequestUri?.AbsolutePath ?? string.Empty;
}
