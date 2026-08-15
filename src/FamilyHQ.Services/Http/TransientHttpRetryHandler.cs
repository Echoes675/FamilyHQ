using System.Globalization;
using System.Net;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Http;

/// <summary>
/// FHQ-114: transient-fault retry for the ip-api, Nominatim and Open-Meteo typed clients, which
/// previously had only a Timeout — so a single 5xx or network blip propagated straight to the caller.
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
/// A 429 is only replayed when the server said when to come back — a quota rejection with no hint
/// is surfaced so the caller's own backoff owns it (FHQ-109 for weather). Every sleep is bounded by
/// <see cref="ExternalHttpResilienceOptions.MaxRetryDelay"/>, on both the response and the
/// exception path, because sleeps happen inside <c>SendAsync</c> and therefore spend the client's
/// TOTAL budget — see <see cref="ExternalHttpResilienceOptions.LocationTimeout"/>.
/// </para>
/// </summary>
public sealed class TransientHttpRetryHandler(
    IOptions<ExternalHttpResilienceOptions> options,
    TimeProvider timeProvider,
    ILogger<TransientHttpRetryHandler> logger) : DelegatingHandler
{
    /// <summary>ip-api.com reports the seconds left in its rate-limit window here, not in Retry-After.</summary>
    private const string RateLimitTtlHeaderName = "X-Ttl";

    private const string NetworkFailureOutcome = "a network failure";

    private readonly ExternalHttpResilienceOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            TimeSpan delay;
            var lastOutcome = NetworkFailureOutcome;
            try
            {
                var response = await base.SendAsync(request, ct);

                if (!IsTransient(response.StatusCode) || !CanRetry(request, attempt))
                    return response;

                var hint = GetServerRetryHint(response);
                if (hint is { } serverHint)
                {
                    if (serverHint > _options.MaxRetryDelay)
                    {
                        logger.LogWarning(
                            "{Host}{Path} asked for a {DelaySeconds}s wait after {StatusCode}; surfacing instead of holding the request open.",
                            Host(request), Path(request), (int)serverHint.TotalSeconds, (int)response.StatusCode);
                        return response;
                    }
                    delay = serverHint;
                }
                else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // A quota rejection with no reset hint (Open-Meteo's shape). Replaying it in-request
                    // would spend more of the same exhausted quota during exactly the overload the
                    // caller's own backoff exists to end.
                    logger.LogWarning(
                        "{Host}{Path} returned 429 with no Retry-After or X-Ttl; surfacing for caller-level backoff.",
                        Host(request), Path(request));
                    return response;
                }
                else
                {
                    delay = ComputeExponentialDelay(attempt);
                    if (delay > _options.MaxRetryDelay)
                        return response;
                }

                lastOutcome = FormattableString.Invariant($"HTTP {(int)response.StatusCode}");
                logger.LogWarning(
                    "Transient {StatusCode} from {Host}{Path}; retry {Attempt}/{MaxAttempts} after {DelayMs}ms.",
                    (int)response.StatusCode, Host(request), Path(request), attempt, _options.MaxAttempts,
                    (int)delay.TotalMilliseconds);

                response.Dispose(); // release the connection before sleeping
            }
            catch (HttpRequestException ex) when (CanRetry(request, attempt))
            {
                delay = ComputeExponentialDelay(attempt);
                if (delay > _options.MaxRetryDelay)
                    throw; // same ceiling the response path applies — never overrun the total budget

                logger.LogWarning(ex,
                    "Transient network failure calling {Host}{Path}; retry {Attempt}/{MaxAttempts} after {DelayMs}ms.",
                    Host(request), Path(request), attempt, _options.MaxAttempts, (int)delay.TotalMilliseconds);
            }

            try
            {
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (OperationCanceledException)
            {
                // The client Timeout (or shutdown) landed mid-sleep, so HttpClient rewrites this into a
                // cancellation and the upstream result never reaches the caller. Record what we were
                // actually retrying so Seq shows the cause, not just "timeout".
                logger.LogWarning(
                    "Retry wait for {Host}{Path} was cancelled after {Outcome}; the caller sees a cancellation, not the upstream result.",
                    Host(request), Path(request), lastOutcome);
                throw;
            }
        }
    }

    /// <summary>
    /// The server's own "come back in" instruction, or null when it gave none. <c>Retry-After</c>
    /// (delta or HTTP-date) first; then ip-api's <c>X-Ttl</c>, but ONLY on a 429 — X-Ttl is the
    /// rate-limit window counter, paired with <c>X-Rl</c> (requests remaining), so it ships on
    /// non-throttled responses too and does not mean "wait" there.
    /// </summary>
    internal TimeSpan? GetServerRetryHint(HttpResponseMessage response)
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

        if (response.StatusCode == HttpStatusCode.TooManyRequests
            && response.Headers.TryGetValues(RateLimitTtlHeaderName, out var ttlValues)
            && int.TryParse(ttlValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttlSeconds)
            && ttlSeconds > 0)
            return TimeSpan.FromSeconds(ttlSeconds);

        return null;
    }

    /// <summary>Exponential backoff with jitter: uniform in [2^(n-1) x BaseDelay, 2^n x BaseDelay).</summary>
    internal TimeSpan ComputeExponentialDelay(int attempt)
    {
        var expMs = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMs = expMs * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(expMs + jitterMs);
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || ((int)status >= 500 && status != HttpStatusCode.NotImplemented);

    private bool CanRetry(HttpRequestMessage request, int attempt)
        => attempt < _options.MaxAttempts && IsIdempotent(request.Method);

    // A replayed POST/PUT/DELETE could duplicate an upstream side effect; all three clients are GETs.
    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get || method == HttpMethod.Head;

    // Host + path only: never the query string, which carries the family's latitude/longitude.
    private static string Host(HttpRequestMessage request) => request.RequestUri?.Host ?? "unknown";

    private static string Path(HttpRequestMessage request) => request.RequestUri?.AbsolutePath ?? string.Empty;
}
