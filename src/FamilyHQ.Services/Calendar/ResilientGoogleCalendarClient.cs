using System.Net;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// FHQ-154: retry decorator over <see cref="IGoogleCalendarClient"/>. Retries transient
/// <see cref="GoogleApiException"/>s (429 / rate-limit 403 for any operation; 5xx only for idempotent
/// operations — never a create/watch, to avoid duplicates), honouring Google's Retry-After. Long waits
/// are rethrown rather than slept in-request (foreground → 503; background → job-level retry). Every
/// other exception (reauth, webhook-not-supported, sync-token-expired, cancellation) propagates un-retried.
/// FHQ-91: a per-attempt HttpClient timeout (TaskCanceledException wrapping TimeoutException) is retried
/// like a 5xx — idempotent operations only. The client's Timeout applies inside each attempt's
/// SendAsync; Retry-After waits happen HERE, between attempts, so the timeout can never cut them short.
/// </summary>
public sealed class ResilientGoogleCalendarClient(
    IGoogleCalendarClient inner,
    IOptions<GoogleResilienceOptions> options,
    TimeProvider timeProvider,
    ILogger<ResilientGoogleCalendarClient> logger) : IGoogleCalendarClient
{
    private enum RetryPolicy { Full, RejectedOnly }

    private readonly GoogleResilienceOptions _options = options.Value;

    // ---- Full-policy (idempotent) operations ----
    public Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "GetCalendars", c => inner.GetCalendarsAsync(c), ct);

    public Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(
        string googleCalendarId, DateTimeOffset? syncWindowStart, DateTimeOffset? syncWindowEnd, string? syncToken = null, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "GetEvents", c => inner.GetEventsAsync(googleCalendarId, syncWindowStart, syncWindowEnd, syncToken, c), ct);

    public Task<CalendarEvent> PatchEventFieldsAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "PatchEventFields", c => inner.PatchEventFieldsAsync(googleCalendarId, calendarEvent, contentHash, c), ct);

    public Task PatchEventFieldsPreservingTimesAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "PatchEventFieldsPreservingTimes", c => inner.PatchEventFieldsPreservingTimesAsync(googleCalendarId, calendarEvent, contentHash, c), ct);

    public Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "DeleteEvent", c => inner.DeleteEventAsync(googleCalendarId, googleEventId, c), ct);

    public Task PatchSeriesRecurrenceAsync(string googleCalendarId, string seriesId, string rrule, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "PatchSeriesRecurrence", c => inner.PatchSeriesRecurrenceAsync(googleCalendarId, seriesId, rrule, c), ct);

    public Task ClearSeriesRecurrenceAsync(string googleCalendarId, string seriesId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "ClearSeriesRecurrence", c => inner.ClearSeriesRecurrenceAsync(googleCalendarId, seriesId, c), ct);

    public Task<GoogleEventDetail?> GetEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "GetEvent", c => inner.GetEventAsync(googleCalendarId, googleEventId, c), ct);

    public Task<SeriesMaster?> GetSeriesMasterAsync(string googleCalendarId, string seriesId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "GetSeriesMaster", c => inner.GetSeriesMasterAsync(googleCalendarId, seriesId, c), ct);

    public Task StopChannelAsync(string channelId, string resourceId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.Full, "StopChannel", c => inner.StopChannelAsync(channelId, resourceId, c), ct);

    // ---- Rejected-only (non-idempotent) operations: never retry 5xx ----
    public Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.RejectedOnly, "CreateEvent", c => inner.CreateEventAsync(googleCalendarId, calendarEvent, contentHash, c), ct);

    public Task<CalendarEvent> CreateRecurringEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, string rrule, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.RejectedOnly, "CreateRecurringEvent", c => inner.CreateRecurringEventAsync(googleCalendarId, calendarEvent, contentHash, rrule, c), ct);

    public Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.RejectedOnly, "MoveEvent", c => inner.MoveEventAsync(sourceCalendarId, googleEventId, destinationCalendarId, c), ct);

    public Task<WatchChannelResponse> WatchEventsAsync(string googleCalendarId, string channelId, string webhookUrl, string channelToken, CancellationToken ct = default)
        => WithRetryAsync(RetryPolicy.RejectedOnly, "WatchEvents", c => inner.WatchEventsAsync(googleCalendarId, channelId, webhookUrl, channelToken, c), ct);

    // ---- Retry core ----
    private Task WithRetryAsync(RetryPolicy policy, string operation, Func<CancellationToken, Task> op, CancellationToken ct)
        => WithRetryAsync<object?>(policy, operation, async c => { await op(c); return null; }, ct);

    private async Task<T> WithRetryAsync<T>(RetryPolicy policy, string operation, Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await op(ct);
            }
            catch (GoogleApiException ex)
            {
                if (attempt >= _options.MaxAttempts || !ShouldRetry(ex, policy))
                    throw;

                var delay = ComputeDelay(ex, attempt);
                if (delay > _options.RetryAfterInRequestCap)
                    throw; // long wait → surface now (foreground 503 / background job retry) rather than block

                logger.LogWarning(
                    "Google {Operation} transient {Status}; retry {Attempt}/{Max} after {DelayMs}ms.",
                    operation, (int)ex.StatusCode, attempt, _options.MaxAttempts, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && !ct.IsCancellationRequested)
            {
                // FHQ-91: per-attempt HttpClient timeout (the inner TimeoutException and untouched
                // caller token distinguish it from cancellation). Like a 5xx, the request may have
                // reached Google — so only idempotent operations retry, never a create/watch.
                if (attempt >= _options.MaxAttempts || policy != RetryPolicy.Full)
                    throw;

                var delay = ComputeExponentialDelay(attempt);
                if (delay > _options.RetryAfterInRequestCap)
                    throw;

                logger.LogWarning(
                    "Google {Operation} attempt timed out; retry {Attempt}/{Max} after {DelayMs}ms.",
                    operation, attempt, _options.MaxAttempts, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    private static bool ShouldRetry(GoogleApiException ex, RetryPolicy policy)
    {
        // 429 and rate-limit 403 mean "rejected, not processed" — safe to retry for any operation.
        // (Post-FHQ-83 a GoogleApiException with 403 is always a rate-limit; auth 403 is GoogleReauthRequiredException.)
        if (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            return true;
        // 5xx may have been processed — only retry idempotent operations, never a create/watch.
        return policy == RetryPolicy.Full && (int)ex.StatusCode >= 500;
    }

    private TimeSpan ComputeDelay(GoogleApiException ex, int attempt)
    {
        if (ex.RetryAfter is { } ra && ra > TimeSpan.Zero)
            return ra;
        return ComputeExponentialDelay(attempt);
    }

    private TimeSpan ComputeExponentialDelay(int attempt)
    {
        // Exponential backoff with jitter: uniform in [exp, 2*exp).
        var expMs = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitterMs = expMs * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(expMs + jitterMs);
    }
}
