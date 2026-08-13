namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Backoff settings for the background SignalR restart loop (FHQ-125).
/// Bound from the optional <c>SignalRReconnect</c> configuration section;
/// the defaults apply when the section is absent.
/// </summary>
public sealed record SignalRReconnectOptions
{
    /// <summary>Delay before the first restart attempt; doubles after each failure.</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound for the exponential backoff between restart attempts.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
}
