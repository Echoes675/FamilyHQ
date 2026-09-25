using FamilyHQ.WebApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Registers everything the preprod smoke token endpoints need (FHQ-139): the tier, smoke-account and
/// endpoint options, the <c>SmokeClient</c> authentication scheme, the <c>PreprodSmokeAccess</c> policy
/// and its requirement handlers — and refuses the boot outright on the one combination that must never
/// exist.
/// </summary>
public static class PreprodSmokeAccessConfiguration
{
    /// <summary>
    /// Binds eagerly and fail-fast validates at boot, following the FHQ-196 <c>SyncOptions.Validate()</c>
    /// precedent in <c>AddFamilyHqServices</c>: the alternative is a misconfiguration that stays
    /// invisible until something reads the options at runtime — which for a guard means never.
    /// </summary>
    public static IServiceCollection AddPreprodSmokeAccess(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var deployment = configuration.GetSection(DeploymentOptions.SectionName)
            .Get<DeploymentOptions>() ?? new DeploymentOptions();
        var endpoint = configuration.GetSection(IssueTokenEndpointOptions.SectionName)
            .Get<IssueTokenEndpointOptions>() ?? new IssueTokenEndpointOptions();

        PreprodSmokeAccessGuard.Validate(endpoint, deployment);

        services.Configure<DeploymentOptions>(configuration.GetSection(DeploymentOptions.SectionName));
        services.Configure<IssueTokenEndpointOptions>(
            configuration.GetSection(IssueTokenEndpointOptions.SectionName));
        services.Configure<SmokeOptions>(configuration.GetSection(SmokeOptions.SectionName));

        // Its own scheme, deliberately not the application default: the shared secret authenticates a
        // machine and must never be able to satisfy a user login.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SmokeClientAuthenticationHandler>(
                SmokeClientAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddAuthorization(options =>
            options.AddPolicy(PreprodSmokeAccessPolicy.Name, PreprodSmokeAccessPolicy.Configure));

        // Stateless; the request they inspect arrives through IHttpContextAccessor.
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddSingleton<ISmokeRequestUserIdReader, SmokeRequestUserIdReader>();
        services.AddSingleton<IAuthorizationHandler, SmokeEndpointAvailableRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, SmokeAccountRequirementHandler>();

        return services;
    }
}
