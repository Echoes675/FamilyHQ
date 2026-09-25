namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// The startup layer of the FHQ-139 guard: a pure, host-free decision called from
/// <c>AddPreprodSmokeAccess</c> at boot, following the <c>SyncOptions.Validate()</c> / FHQ-196
/// fail-fast precedent.
/// <para>
/// It exists because the prod deploy pipeline's config check (layer one) protects the pipeline, not
/// a container someone starts by hand. If a prod container is ever handed preprod's env file — the
/// realistic accident, since that file carries the shared secret with it — the process must refuse to
/// start rather than serve token-minting endpoints on the family's live data.
/// </para>
/// </summary>
public static class PreprodSmokeAccessGuard
{
    /// <summary>
    /// Refuses the two configurations that cannot be intended. Deliberately does NOT require a tier
    /// to be present, nor <c>Smoke:UserId</c> to be set: both of those deny at the authorization
    /// policy, and crash-looping an environment over them would make rolling this change out ahead of
    /// the env credentials impossible.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The endpoints are enabled on the production tier, or enabled without a shared secret.
    /// </exception>
    public static void Validate(IssueTokenEndpointOptions endpoint, DeploymentOptions deployment)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(deployment);

        if (!endpoint.Enabled)
            return;

        if (DeploymentOptions.IsProductionTier(deployment.Tier))
            throw new InvalidOperationException(
                $"{IssueTokenEndpointOptions.SectionName}:{nameof(IssueTokenEndpointOptions.Enabled)} " +
                $"is true while {DeploymentOptions.SectionName}:{nameof(DeploymentOptions.Tier)} is " +
                $"'{DeploymentOptions.ProductionTier}'. The preprod smoke token endpoints mint credentials " +
                "without interactive sign-in and must never be reachable in production — disable the " +
                "endpoint or correct the tier.");

        // The secret value never travels on the message: a startup exception reaches Seq via whatever
        // logs the boot failure.
        if (string.IsNullOrWhiteSpace(endpoint.Secret))
            throw new InvalidOperationException(
                $"{IssueTokenEndpointOptions.SectionName}:{nameof(IssueTokenEndpointOptions.Secret)} " +
                $"must be configured when {nameof(IssueTokenEndpointOptions.Enabled)} is true; an enabled " +
                "endpoint with no secret would reject every caller while looking configured.");
    }
}
