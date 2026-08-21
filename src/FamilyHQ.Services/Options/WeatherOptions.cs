namespace FamilyHQ.Services.Options;

public class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Hard ceiling on <see cref="MaxFailureBackoffMinutes"/>: one day.</summary>
    private const int MaxFailureBackoffCeilingMinutes = 1440;

    /// <summary>
    /// Hard ceiling on both retention windows: one day, matching
    /// <see cref="MaxFailureBackoffCeilingMinutes"/>. Without an upper bound a typo such as 525600
    /// validates cleanly and silently disables retention altogether — the kiosk would then show a
    /// year-old forecast rather than hiding it, which is the failure this setting exists to prevent.
    /// Nothing this feature does is served by keeping weather for longer than a day.
    /// </summary>
    private const int StaleAfterCeilingMinutes = 1440;

    public string BaseUrl { get; set; } = "https://api.open-meteo.com";
    public int PollIntervalMinutes { get; set; } = 30;
    public int MinPollIntervalMinutes { get; set; } = 1;
    public double WindThresholdKmh { get; set; } = 30;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// FHQ-109: factor a user's poll interval is multiplied by for each consecutive
    /// <c>RefreshAsync</c> failure. 2 doubles the gap every failure; 1 disables escalation.
    /// </summary>
    public double FailureBackoffMultiplier { get; set; } = 2;

    /// <summary>
    /// FHQ-109: ceiling on a failing user's escalated poll interval. With the defaults a user on the
    /// 1-minute floor backs off 2 → 4 → 8 … → 60 minutes, so a permanently rate-limited Open-Meteo
    /// costs at most <c>ExternalHttpResilienceOptions.MaxAttempts</c> requests per hour for that
    /// user instead of 60 requests per hour. A single successful refresh resets to the configured
    /// interval. Must be at least <see cref="MinPollIntervalMinutes"/> and at most
    /// <see cref="MaxFailureBackoffCeilingMinutes"/> — a longer wait than <c>Task.Delay</c> can
    /// express would throw inside the loop, which then catches and re-enters every minute forever:
    /// exactly the spin this setting exists to remove.
    /// </summary>
    public int MaxFailureBackoffMinutes { get; set; } = 60;

    /// <summary>
    /// FHQ-159: how long a stored forecast section (<c>Hourly</c>, <c>Daily</c>) keeps being shown
    /// after the refresh that produced it. Past this it is hidden entirely — no stale marker, no
    /// "last updated" indicator. 360 minutes is 12 consecutive missed polls at the 30-minute
    /// production interval, so no realistic upstream blip can blank the kiosk, while nothing
    /// visibly wrong survives long enough to mislead.
    /// </summary>
    public int ForecastStaleAfterMinutes { get; set; } = 360;

    /// <summary>
    /// FHQ-159: how long the stored <c>Current</c> reading keeps being shown after the refresh that
    /// produced it. Deliberately tighter than <see cref="ForecastStaleAfterMinutes"/> because,
    /// unlike a forecast, it asserts something about <i>now</i> — an hours-old temperature is wrong
    /// rather than merely old. 60 minutes is 2 missed polls at the production interval.
    /// </summary>
    public int CurrentStaleAfterMinutes { get; set; } = 60;

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(BaseUrl)} must be configured.");

        if (PollIntervalMinutes < 1)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(PollIntervalMinutes)} must be at least 1 (was {PollIntervalMinutes}).");

        if (MinPollIntervalMinutes < 1)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(MinPollIntervalMinutes)} must be at least 1 (was {MinPollIntervalMinutes}).");

        if (WindThresholdKmh < 0)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(WindThresholdKmh)} must not be negative (was {WindThresholdKmh}).");

        if (FailureBackoffMultiplier < 1)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(FailureBackoffMultiplier)} must be at least 1 (was {FailureBackoffMultiplier}).");

        if (MaxFailureBackoffMinutes < MinPollIntervalMinutes
            || MaxFailureBackoffMinutes > MaxFailureBackoffCeilingMinutes)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(MaxFailureBackoffMinutes)} must be between " +
                $"{nameof(MinPollIntervalMinutes)} ({MinPollIntervalMinutes}) and " +
                $"{MaxFailureBackoffCeilingMinutes} (was {MaxFailureBackoffMinutes}).");

        // Zero or negative would hide every section the instant it was written, which reads on the
        // kiosk as "weather is broken"; anything past the ceiling disables retention entirely and
        // leaves stale data on the wall. Surface both at boot instead.
        if (ForecastStaleAfterMinutes is < 1 or > StaleAfterCeilingMinutes)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(ForecastStaleAfterMinutes)} must be between 1 and " +
                $"{StaleAfterCeilingMinutes} (was {ForecastStaleAfterMinutes}).");

        if (CurrentStaleAfterMinutes is < 1 or > StaleAfterCeilingMinutes)
            throw new InvalidOperationException(
                $"{nameof(WeatherOptions)}.{nameof(CurrentStaleAfterMinutes)} must be between 1 and " +
                $"{StaleAfterCeilingMinutes} (was {CurrentStaleAfterMinutes}).");
    }
}
