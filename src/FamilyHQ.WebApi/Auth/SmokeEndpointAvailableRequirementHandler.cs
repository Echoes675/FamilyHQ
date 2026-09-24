using FamilyHQ.WebApi.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Evaluates <see cref="SmokeEndpointAvailableRequirement"/> and audits the decision. Never logs the
/// shared secret — only the flag and the tier, both of which are operational facts an operator needs
/// when a smoke run reports 404.
/// </summary>
public sealed class SmokeEndpointAvailableRequirementHandler(
    IOptions<IssueTokenEndpointOptions> endpointOptions,
    IOptions<DeploymentOptions> deploymentOptions,
    ILogger<SmokeEndpointAvailableRequirementHandler> logger)
    : AuthorizationHandler<SmokeEndpointAvailableRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SmokeEndpointAvailableRequirement requirement)
    {
        var enabled = endpointOptions.Value.Enabled;
        var tier = deploymentOptions.Value.Tier;

        if (SmokeEndpointAvailableRequirement.IsSatisfied(enabled, tier))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Information, not Warning: on every environment with the endpoints switched off — which is
        // all of them but preprod — this is the expected answer to a stray request.
        logger.LogInformation(
            "Preprod smoke access denied — the endpoints are not available on this deployment "
            + "(enabled: {SmokeEndpointEnabled}, tier: {DeploymentTier}).",
            enabled, tier ?? "(unset)");
        return Task.CompletedTask;
    }
}
