using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Xunit;

// Deliberately in the test-root namespace, matching JwtSessionOptionsTests: a
// "FamilyHQ.WebApi.Tests.Configuration" namespace would shadow types for sibling namespaces.
namespace FamilyHQ.WebApi.Tests;

/// <summary>
/// FHQ-139. The startup layer of the three-layer guard, exercised through the pure decision so it
/// needs no host: the same image is promoted preprod → prod, so the only thing that can stop a prod
/// container serving the smoke endpoints is configuration.
/// </summary>
public class PreprodSmokeAccessGuardTests
{
    private const string Secret = "a-long-enough-shared-secret-value";

    [Fact]
    public void Validate_WhenTierIsProdAndEndpointEnabled_Throws()
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: true), Deployment("prod"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*prod*");
    }

    [Fact]
    public void Validate_WhenTierIsProdAndEndpointDisabled_DoesNotThrow()
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: false), Deployment("prod"));

        act.Should().NotThrow("production boots normally with the endpoints switched off");
    }

    [Fact]
    public void Validate_WhenTierIsPreprodAndEndpointEnabled_DoesNotThrow()
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: true), Deployment("preprod"));

        act.Should().NotThrow("preprod is the environment the endpoints exist for");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_WhenTierIsNotSetAndEndpointDisabled_DoesNotThrow(string? tier)
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: false), Deployment(tier));

        act.Should().NotThrow(
            "rolling this change out before every env credential carries a tier must not crash-loop " +
            "an environment — a missing tier denies at the policy instead");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenEndpointEnabledWithoutASecret_Throws(string? secret)
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: true, secret: secret), Deployment("preprod"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Secret*");
    }

    [Fact]
    public void Validate_WhenEndpointDisabledWithoutASecret_DoesNotThrow()
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: false, secret: null), Deployment("dev"));

        act.Should().NotThrow("a disabled endpoint needs no secret");
    }

    [Fact]
    public void Validate_WhenItThrows_TheMessageDoesNotCarryTheSecret()
    {
        var act = () => PreprodSmokeAccessGuard.Validate(
            Endpoint(enabled: true, secret: Secret), Deployment("prod"));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(Secret,
                "a startup exception message reaches Seq via whatever logs the boot failure");
    }

    private static IssueTokenEndpointOptions Endpoint(bool enabled, string? secret = Secret) =>
        new() { Enabled = enabled, Secret = secret };

    private static DeploymentOptions Deployment(string? tier) => new() { Tier = tier };
}
