namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// The explicit deployment tier (FHQ-139). Preprod deliberately runs with
/// <c>ASPNETCORE_ENVIRONMENT=Production</c> so that it loads production settings and takes
/// production code paths — which is exactly why <c>IHostEnvironment.IsProduction()</c> cannot gate
/// the preprod smoke endpoints: to it, preprod and prod are indistinguishable. The same image is
/// promoted from preprod to prod, so the endpoints cannot be compiled out either; they are switched
/// by this key alone.
/// <para>
/// <b>A missing or unrecognised tier counts as not-allowed.</b> Forgetting the key, or mistyping it,
/// fails safe: the smoke endpoints stay shut. That also means this change can be deployed before
/// every environment's env credential carries a tier — nothing breaks, the endpoints simply remain
/// unavailable.
/// </para>
/// </summary>
public class DeploymentOptions
{
    public const string SectionName = "Deployment";

    /// <summary>The one tier the smoke endpoints must never be reachable on.</summary>
    public const string ProductionTier = "prod";

    private static readonly string[] KnownTiers = ["dev", "staging", "preprod", ProductionTier];

    /// <summary>
    /// <c>dev</c> | <c>staging</c> | <c>preprod</c> | <c>prod</c>. Nullable because a deployment that
    /// has not been given the key must still boot (see the class remarks).
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// Pure decision: may the preprod smoke endpoints serve on this tier? True only for a tier that
    /// is recognised and is not production.
    /// </summary>
    public static bool TierAllowsSmokeEndpoints(string? tier)
    {
        var normalised = tier?.Trim();
        if (string.IsNullOrEmpty(normalised))
            return false;

        return KnownTiers.Contains(normalised, StringComparer.OrdinalIgnoreCase)
            && !IsProductionTier(normalised);
    }

    /// <summary>
    /// Pure decision: is this the production tier? Used by the startup refusal, which must fire only
    /// on an unambiguous "this is prod" — every other value is handled by the fail-safe deny in
    /// <c>TierAllowsSmokeEndpoints</c>.
    /// </summary>
    public static bool IsProductionTier(string? tier) =>
        string.Equals(tier?.Trim(), ProductionTier, StringComparison.OrdinalIgnoreCase);
}
