namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Fixed-window limit for one named rate-limiting policy (FHQ-101). Bound from a sub-section of
/// <see cref="RateLimitingOptions.SectionName"/>; defaults are supplied by the property
/// initialisers on <see cref="RateLimitingOptions"/>.
/// </summary>
public sealed class RateLimitPolicyOptions
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromHours(1);

    /// <summary>Requests allowed per <see cref="Window"/> per partition (IP or user).</summary>
    public int PermitLimit { get; set; }

    /// <summary>Fixed window over which <see cref="PermitLimit"/> applies.</summary>
    public TimeSpan Window { get; set; }

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    internal void Validate(string policyPropertyName)
    {
        if (PermitLimit < 1)
            throw new InvalidOperationException(
                $"{nameof(RateLimitingOptions)}.{policyPropertyName}.{nameof(PermitLimit)} must be at least 1 (was {PermitLimit}).");

        if (Window <= TimeSpan.Zero || Window > MaxWindow)
            throw new InvalidOperationException(
                $"{nameof(RateLimitingOptions)}.{policyPropertyName}.{nameof(Window)} must be positive and at most {MaxWindow} (was {Window}).");
    }
}
