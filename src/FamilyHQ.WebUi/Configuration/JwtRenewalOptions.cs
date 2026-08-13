namespace FamilyHQ.WebUi.Configuration;

/// <summary>
/// Sliding JWT renewal settings (FHQ-126). Defaults: the server mints 365-day tokens, and with a
/// 358-day threshold the kiosk renews once the token is more than ~7 days old — so a kiosk in
/// regular use always carries a nearly full-life token and never reaches the expiry cliff.
/// </summary>
public class JwtRenewalOptions
{
    public const string SectionName = "JwtRenewal";

    /// <summary>Renew when the token's remaining lifetime drops below this many days.</summary>
    public double RenewalThresholdDays { get; set; } = 358;

    /// <summary>How often the background loop re-checks the token's remaining lifetime.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Fail-fast guard, called at service construction so bad config surfaces at startup.</summary>
    public void Validate()
    {
        if (RenewalThresholdDays <= 0)
            throw new InvalidOperationException(
                $"{nameof(JwtRenewalOptions)}.{nameof(RenewalThresholdDays)} must be positive (was {RenewalThresholdDays}).");
        if (CheckInterval <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"{nameof(JwtRenewalOptions)}.{nameof(CheckInterval)} must be positive (was {CheckInterval}).");
    }
}
