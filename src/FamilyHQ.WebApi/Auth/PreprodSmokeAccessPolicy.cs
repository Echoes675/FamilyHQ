using Microsoft.AspNetCore.Authorization;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// The named authorization policy that guards both preprod smoke token endpoints (FHQ-139), applied
/// with <c>[Authorize(Policy = PreprodSmokeAccessPolicy.Name)]</c> rather than hand-rolled checks
/// inside the actions — so the guard cannot be forgotten on a new action, and an action can never run
/// with one of the three requirements unevaluated.
/// <para>
/// A shared secret on its own is weak against the realistic accident: the likeliest way these endpoints
/// end up enabled in production is someone copying preprod's env file, which carries the secret with
/// it. So the secret is one requirement of three, and the other two (a non-production tier, and a
/// request that names only the smoke account) do not travel in that file's <c>Secret</c> line.
/// </para>
/// </summary>
public static class PreprodSmokeAccessPolicy
{
    public const string Name = "PreprodSmokeAccess";

    /// <summary>
    /// Requirement one is the tier + opt-in flag, requirement two is the shared secret (an
    /// authenticated <c>SmokeClient</c> principal — and ONLY that scheme, so a valid FamilyHQ user JWT
    /// cannot satisfy the policy), requirement three is the smoke-account allowlist.
    /// </summary>
    public static void Configure(AuthorizationPolicyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAuthenticationSchemes(SmokeClientAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new SmokeEndpointAvailableRequirement(), new SmokeAccountRequirement());
    }
}
