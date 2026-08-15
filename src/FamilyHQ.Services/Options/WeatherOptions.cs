namespace FamilyHQ.Services.Options;

public class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Hard ceiling on <see cref="MaxFailureBackoffMinutes"/>: one day.</summary>
    private const int MaxFailureBackoffCeilingMinutes = 1440;

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
    }
}
