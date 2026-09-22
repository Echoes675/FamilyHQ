using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

// Deliberately in the test-root namespace, matching JwtSessionOptionsTests: a
// "FamilyHQ.WebApi.Tests.Configuration" namespace would shadow types for sibling namespaces.
namespace FamilyHQ.WebApi.Tests;

/// <summary>
/// FHQ-139. Wiring tests for the PreprodSmokeAccess policy: the composition IS the security property
/// here, so the policy's scheme and its requirement set are pinned.
/// </summary>
public class PreprodSmokeAccessConfigurationTests
{
    private const string Secret = "smoke-shared-secret-8f3c1d9a";
    private const string SmokeUserId = "smoke-account-google-sub";

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_DefinesThePreprodSmokeAccessPolicy()
    {
        var policy = ResolvePolicy(Settings());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_ThePolicyAcceptsOnlyTheSmokeClientScheme()
    {
        var policy = ResolvePolicy(Settings());

        policy!.AuthenticationSchemes.Should().Equal([SmokeClientAuthenticationHandler.SchemeName],
            "a valid FamilyHQ user JWT must never satisfy this policy — the JWT scheme is not consulted");
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_ThePolicyRequiresAllThreeRequirements()
    {
        var policy = ResolvePolicy(Settings());

        policy!.Requirements.Select(requirement => requirement.GetType()).Should().BeEquivalentTo(
            new[]
            {
                typeof(DenyAnonymousAuthorizationRequirement),
                typeof(SmokeEndpointAvailableRequirement),
                typeof(SmokeAccountRequirement)
            },
            "the shared secret (an authenticated SmokeClient principal), the tier + flag, and the "
            + "smoke-account allowlist must all pass");
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_RegistersTheSmokeClientAuthenticationScheme()
    {
        using var provider = BuildProvider(Settings());

        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        authentication.SchemeMap.Should().ContainKey(SmokeClientAuthenticationHandler.SchemeName);
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_DoesNotChangeTheDefaultAuthenticationScheme()
    {
        using var provider = BuildProvider(Settings());

        var authentication = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        authentication.DefaultScheme.Should().NotBe(SmokeClientAuthenticationHandler.SchemeName,
            "the smoke scheme is opt-in per endpoint; it must never become the application default");
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_RegistersBothRequirementHandlers()
    {
        using var provider = BuildProvider(Settings());

        var handlers = provider.GetServices<IAuthorizationHandler>().Select(handler => handler.GetType());

        handlers.Should().Contain(typeof(SmokeEndpointAvailableRequirementHandler))
            .And.Contain(typeof(SmokeAccountRequirementHandler));
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenRegistered_BindsTheConfiguredTierSecretAndSmokeAccount()
    {
        using var provider = BuildProvider(Settings(tier: "preprod", enabled: true));

        provider.GetRequiredService<IOptions<DeploymentOptions>>().Value.Tier.Should().Be("preprod");
        provider.GetRequiredService<IOptions<IssueTokenEndpointOptions>>().Value.Enabled.Should().BeTrue();
        provider.GetRequiredService<IOptions<IssueTokenEndpointOptions>>().Value.Secret.Should().Be(Secret);
        provider.GetRequiredService<IOptions<SmokeOptions>>().Value.UserId.Should().Be(SmokeUserId);
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenTheTierIsProdAndTheEndpointIsEnabled_RefusesToRegister()
    {
        var act = () => BuildProvider(Settings(tier: "prod", enabled: true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*prod*",
                "the startup guard must stop the process before it can serve token-minting endpoints "
                + "against the family's live data");
    }

    [Fact]
    public void AddPreprodSmokeAccess_WhenTheTierIsProdAndTheEndpointIsDisabled_Registers()
    {
        var act = () => BuildProvider(Settings(tier: "prod", enabled: false));

        act.Should().NotThrow();
    }

    private static Dictionary<string, string?> Settings(string tier = "preprod", bool enabled = true) =>
        new()
        {
            [$"{DeploymentOptions.SectionName}:{nameof(DeploymentOptions.Tier)}"] = tier,
            [$"{IssueTokenEndpointOptions.SectionName}:{nameof(IssueTokenEndpointOptions.Enabled)}"] =
                enabled.ToString(),
            [$"{IssueTokenEndpointOptions.SectionName}:{nameof(IssueTokenEndpointOptions.Secret)}"] = Secret,
            [$"{SmokeOptions.SectionName}:{nameof(SmokeOptions.UserId)}"] = SmokeUserId
        };

    private static AuthorizationPolicy? ResolvePolicy(Dictionary<string, string?> settings)
    {
        using var provider = BuildProvider(settings);
        return provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value
            .GetPolicy(PreprodSmokeAccessPolicy.Name);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPreprodSmokeAccess(configuration);
        return services.BuildServiceProvider();
    }
}
