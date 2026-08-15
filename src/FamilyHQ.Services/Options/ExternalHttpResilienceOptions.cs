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
    /// Longest the handler will sleep between attempts. A server-supplied Retry-After (or ip-api's
    /// X-Ttl) longer than this stops the retry and surfaces the response instead — a rate-limit
    /// window measured in minutes is for the caller's own backoff to absorb, not an in-request sleep.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// TOTAL HttpClient budget for one ip-api call. Unlike the Google clients — whose retry
    /// decorator sits ABOVE HttpClient, making their timeout per-attempt — this handler sleeps and
    /// re-sends inside <c>SendAsync</c>, so the client Timeout bounds the whole attempt+backoff
    /// sequence. Worst case with the defaults: 3 attempts plus 2 capped sleeps
    /// (2 x 5s = 10s of sleeping) inside 30s. That is the hard ceiling on
    /// <c>LocationService.GetEffectiveLocationAsync</c>, which sits on the DayThemeScheduler hot
    /// path and on the settings location-autodetect request.
    /// </summary>
    public TimeSpan LocationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>TOTAL HttpClient budget for one Nominatim geocode call — see <see cref="LocationTimeout"/>.</summary>
    public TimeSpan GeocodingTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// TOTAL HttpClient budget for one Open-Meteo forecast call (a 16-day payload, hence the larger
    /// figure than <see cref="LocationTimeout"/>). This is also the point where the two FHQ-109 /
    /// FHQ-114 layers meet: retry is spent INSIDE a single poll attempt (max 60s, max
    /// <see cref="MaxAttempts"/> requests), while the poller's per-user backoff only ever grows the
    /// gap BETWEEN attempts (WeatherOptions.MinPollIntervalMinutes upward to
    /// WeatherOptions.MaxFailureBackoffMinutes). They add, they never multiply: worst case a
    /// permanently rate-limited user costs one 60s window and 3 Open-Meteo requests per poll
    /// interval, i.e. 3 requests/hour once backed off to the 60-minute cap — down from 60/hour.
    /// </summary>
    public TimeSpan WeatherTimeout { get; set; } = TimeSpan.FromSeconds(60);

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
