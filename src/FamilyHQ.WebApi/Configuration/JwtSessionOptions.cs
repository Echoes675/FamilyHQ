namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Absolute cap on renewable session age (FHQ-126 security review). Sliding JWT renewal must not
/// turn a leaked token into permanent access: the auth_time claim carries the ORIGINAL
/// authentication instant through every renewal, and once total session age exceeds this cap the
/// renew-jwt endpoint returns 401 — forcing the kiosk through the full OAuth login again.
/// </summary>
public class JwtSessionOptions
{
    public const string SectionName = "JwtSession";

    /// <summary>Maximum total session age (days) before renewal is refused. Default 2 years.</summary>
    public double MaxSessionAgeDays { get; set; } = 730;

    /// <summary>Fail-fast guard, called at startup so bad config surfaces at boot.</summary>
    public void Validate()
    {
        if (MaxSessionAgeDays <= 0)
            throw new InvalidOperationException(
                $"{nameof(JwtSessionOptions)}.{nameof(MaxSessionAgeDays)} must be positive (was {MaxSessionAgeDays}).");
    }
}
