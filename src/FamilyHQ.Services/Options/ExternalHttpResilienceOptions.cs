namespace FamilyHQ.Services.Options;

/// <summary>
/// FHQ-114: retry and total-budget settings for the three non-Google outbound HTTP clients —
/// ip-api (<c>ILocationService</c>), Nominatim (<c>IGeocodingService</c>) and Open-Meteo
/// (<c>IWeatherProvider</c>). The retry itself is a <c>TransientHttpRetryHandler</c> on each typed
/// client. Defaults apply when no <c>ExternalHttpResilience</c> config section is present.
/// </summary>
public sealed class ExternalHttpResilienceOptions
{
    public const string SectionName = "ExternalHttpResilience";

    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Total attempts including the first (so 3 = 1 try + 2 retries).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base for exponential backoff when the server supplies no Retry-After / X-Ttl hint.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Longest the handler will sleep between attempts, applied on BOTH the response and the
    /// connection-failure path. A server-supplied Retry-After (or ip-api's X-Ttl) longer than this
    /// stops the retry and surfaces the response instead — a rate-limit window measured in minutes
    /// is for the caller's own backoff to absorb, not an in-request sleep. Must be at least
    /// <see cref="BaseDelay"/>, or the first backoff already breaches the ceiling and every retry
    /// silently degenerates into "surface immediately".
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// TOTAL HttpClient budget for one ip-api call. Unlike the Google clients — whose retry
    /// decorator sits ABOVE HttpClient, making their timeout per-attempt — this handler sleeps and
    /// re-sends inside <c>SendAsync</c>, so the client Timeout bounds the whole attempt+backoff
    /// sequence. Worst case with the defaults: 3 attempts plus 2 capped sleeps (&lt; 1s then &lt; 2s)
    /// inside 12s.
    /// <para>
    /// Sized for the INTERACTIVE caller, not the background one. <c>GET /api/settings/location</c>
    /// awaits <c>GetEffectiveLocationAsync</c> whenever the user has no saved location — kiosk
    /// onboarding and every settings load — and the WebUi sets no client-side timeout, so this
    /// figure is exactly how long a user can watch a spinner. It stays close to the 10s
    /// single-attempt ceiling that predated retries. (DayThemeScheduler also calls ip-api, but
    /// <c>EnsureTodayAsync</c> short-circuits once today's record exists, so that path hits the
    /// network roughly once a day and is not what bounds this value.)
    /// </para>
    /// </summary>
    public TimeSpan LocationTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// TOTAL HttpClient budget for one Nominatim geocode call. Also interactive —
    /// <c>POST /api/settings/location</c> awaits it before saving — so it is sized like
    /// <see cref="LocationTimeout"/>.
    /// </summary>
    public TimeSpan GeocodingTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>
    /// TOTAL HttpClient budget for one Open-Meteo forecast call (a 16-day payload, hence the larger
    /// figure than <see cref="LocationTimeout"/>) — deliberately the same 30s ceiling this client
    /// had before retries existed, now covering the whole sequence rather than one attempt.
    /// This is also the point where the two FHQ-109 / FHQ-114 layers meet: retry is spent INSIDE a
    /// single poll attempt (max 30s, max <see cref="MaxAttempts"/> requests), while the poller's
    /// per-user backoff only ever grows the gap BETWEEN attempts
    /// (WeatherOptions.MinPollIntervalMinutes upward to WeatherOptions.MaxFailureBackoffMinutes).
    /// They add, they never multiply: worst case a permanently rate-limited user costs one 30s
    /// window and 3 Open-Meteo requests per poll interval, i.e. 3 requests/hour once backed off to
    /// the 60-minute cap — down from 60/hour. In practice a hintless Open-Meteo 429 is not retried
    /// at all (see <c>TransientHttpRetryHandler</c>), so that ceiling is rarely approached.
    /// </summary>
    public TimeSpan WeatherTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    public void Validate()
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{nameof(MaxAttempts)} must be at least 1 (was {MaxAttempts}).");

        if (BaseDelay < TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{nameof(BaseDelay)} must not be negative (was {BaseDelay}).");

        if (MaxRetryDelay <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{nameof(MaxRetryDelay)} must be positive (was {MaxRetryDelay}).");

        if (BaseDelay > MaxRetryDelay)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{nameof(BaseDelay)} ({BaseDelay}) must not exceed " +
                $"{nameof(MaxRetryDelay)} ({MaxRetryDelay}), or the first backoff already breaches the ceiling " +
                "and no retry ever sleeps.");

        ValidateTimeout(LocationTimeout, nameof(LocationTimeout));
        ValidateTimeout(GeocodingTimeout, nameof(GeocodingTimeout));
        ValidateTimeout(WeatherTimeout, nameof(WeatherTimeout));
    }

    private void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > MaxTimeout)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{name} must be positive and at most {MaxTimeout} (was {value}).");

        // A total budget that cannot even cover the capped inter-attempt sleeps would silently
        // truncate the retry sequence into a timeout — surface it at boot instead.
        var worstCaseSleeps = (MaxAttempts - 1) * MaxRetryDelay;
        if (value <= worstCaseSleeps)
            throw new InvalidOperationException(
                $"{nameof(ExternalHttpResilienceOptions)}.{name} ({value}) must exceed the worst-case " +
                $"inter-attempt sleeps of {worstCaseSleeps} ({nameof(MaxAttempts)} {MaxAttempts}, " +
                $"{nameof(MaxRetryDelay)} {MaxRetryDelay}).");
    }
}
