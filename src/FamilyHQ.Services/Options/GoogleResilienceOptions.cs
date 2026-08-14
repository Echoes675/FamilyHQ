namespace FamilyHQ.Services.Options;

/// <summary>
/// Retry and per-attempt timeout settings for the Google HTTP clients. Retry settings drive the
/// <c>ResilientGoogleCalendarClient</c> decorator (FHQ-154); the timeouts are applied to the
/// <c>GoogleAuthService</c> and <c>GoogleCalendarClient</c> typed HttpClients (FHQ-91). Defaults
/// apply when no <c>GoogleResilience</c> config section is present.
/// </summary>
public sealed class GoogleResilienceOptions
{
    public const string SectionName = "GoogleResilience";

    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Total attempts including the first (so 3 = 1 try + 2 retries).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base for exponential backoff when Google supplies no Retry-After.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>If the computed delay exceeds this, rethrow instead of sleeping in-request.</summary>
    public TimeSpan RetryAfterInRequestCap { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// HttpClient timeout for the Google OAuth client (<c>GoogleAuthService</c>). Not wrapped by
    /// the retry decorator, so this bounds a hung token/login call to a single attempt (FHQ-91).
    /// </summary>
    public TimeSpan AuthTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// PER-ATTEMPT HttpClient timeout for <c>GoogleCalendarClient</c>: each retry the decorator
    /// makes is a full HttpClient call, so worst-case wall time per operation is
    /// MaxAttempts × CalendarTimeout plus the inter-attempt sleeps (each capped at
    /// <see cref="RetryAfterInRequestCap"/>, longer waits rethrow). Defaults: 3 × 45s + 2 × 5s
    /// = 145s (~2.4 min) — deliberately tuned below the sync worker's 5-minute
    /// OrphanRecoveryThreshold, with headroom because each attempt may also spend up to
    /// <see cref="AuthTimeout"/> refreshing the access token inside the same call.
    /// </summary>
    public TimeSpan CalendarTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    public void Validate()
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException(
                $"{nameof(GoogleResilienceOptions)}.{nameof(MaxAttempts)} must be at least 1 (was {MaxAttempts}).");

        if (BaseDelay < TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(GoogleResilienceOptions)}.{nameof(BaseDelay)} must not be negative (was {BaseDelay}).");

        if (RetryAfterInRequestCap <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(GoogleResilienceOptions)}.{nameof(RetryAfterInRequestCap)} must be positive (was {RetryAfterInRequestCap}).");

        ValidateTimeout(AuthTimeout, nameof(AuthTimeout));
        ValidateTimeout(CalendarTimeout, nameof(CalendarTimeout));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > MaxTimeout)
            throw new InvalidOperationException(
                $"{nameof(GoogleResilienceOptions)}.{name} must be positive and at most {MaxTimeout} (was {value}).");
    }
}
