using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Xunit;

// Deliberately in the test-root namespace, matching JwtSessionOptionsTests: a
// "FamilyHQ.WebApi.Tests.Configuration" namespace would shadow types for sibling namespaces.
namespace FamilyHQ.WebApi.Tests;

/// <summary>
/// FHQ-139. The deployment tier is the ONLY thing that distinguishes preprod from prod at runtime —
/// both run with ASPNETCORE_ENVIRONMENT=Production, so IsProduction() cannot be the guard.
/// </summary>
public class DeploymentOptionsTests
{
    [Theory]
    [InlineData("dev")]
    [InlineData("staging")]
    [InlineData("preprod")]
    public void TierAllowsSmokeEndpoints_WhenTierIsANonProductionTier_ReturnsTrue(string tier)
    {
        DeploymentOptions.TierAllowsSmokeEndpoints(tier).Should().BeTrue();
    }

    [Theory]
    [InlineData("PREPROD")]
    [InlineData("  preprod  ")]
    public void TierAllowsSmokeEndpoints_WhenTierIsPreprodInAnyCasingOrPadding_ReturnsTrue(string tier)
    {
        DeploymentOptions.TierAllowsSmokeEndpoints(tier).Should().BeTrue(
            "an env-file value may carry stray padding or casing and must not silently change the tier");
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    public void TierAllowsSmokeEndpoints_WhenTierIsProduction_ReturnsFalse(string tier)
    {
        DeploymentOptions.TierAllowsSmokeEndpoints(tier).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TierAllowsSmokeEndpoints_WhenTierIsNotSet_ReturnsFalse(string? tier)
    {
        DeploymentOptions.TierAllowsSmokeEndpoints(tier).Should().BeFalse(
            "forgetting the key must fail safe — an unset tier is not a permission");
    }

    [Theory]
    [InlineData("production")]
    [InlineData("pre-prod")]
    [InlineData("qa")]
    public void TierAllowsSmokeEndpoints_WhenTierIsUnrecognised_ReturnsFalse(string tier)
    {
        DeploymentOptions.TierAllowsSmokeEndpoints(tier).Should().BeFalse(
            "an unrecognised tier is a typo, and a typo must not open the endpoints");
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData(" prod ")]
    public void IsProductionTier_WhenTierIsProduction_ReturnsTrue(string tier)
    {
        DeploymentOptions.IsProductionTier(tier).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("preprod")]
    [InlineData("production")]
    public void IsProductionTier_WhenTierIsAnythingElse_ReturnsFalse(string? tier)
    {
        DeploymentOptions.IsProductionTier(tier).Should().BeFalse(
            "only the exact prod tier may trip the startup refusal; anything else is handled by the " +
            "fail-safe deny in the authorization policy");
    }
}
