namespace FamilyHQ.Services.Options;

/// <summary>
/// Per-request retry settings for the Google Calendar client decorator (FHQ-154). Defaults apply
/// when no <c>GoogleResilience</c> config section is present.
/// </summary>
public sealed class GoogleResilienceOptions
{
    public const string SectionName = "GoogleResilience";

    /// <summary>Total attempts including the first (so 3 = 1 try + 2 retries).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base for exponential backoff when Google supplies no Retry-After.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>If the computed delay exceeds this, rethrow instead of sleeping in-request.</summary>
    public TimeSpan RetryAfterInRequestCap { get; set; } = TimeSpan.FromSeconds(5);
}
