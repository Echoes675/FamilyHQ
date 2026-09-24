using FamilyHQ.WebApi.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// FHQ-139 requirement one: the smoke endpoints are available at all. Both inputs are configuration —
/// the opt-in flag and the deployment tier — and the tier is re-checked here, not only at startup, so
/// the policy refuses on the production tier even if the flag arrived with a copied env file.
/// </summary>
public sealed class SmokeEndpointAvailableRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Pure decision, so the rule can be read and tested without booting the host.
    /// </summary>
    public static bool IsSatisfied(bool endpointEnabled, string? deploymentTier) =>
        endpointEnabled && DeploymentOptions.TierAllowsSmokeEndpoints(deploymentTier);
}
