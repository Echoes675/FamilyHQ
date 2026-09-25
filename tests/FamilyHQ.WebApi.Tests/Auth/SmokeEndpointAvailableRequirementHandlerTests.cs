using System.Security.Claims;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Auth;

/// <summary>
/// FHQ-139. The tier is re-evaluated INSIDE the policy, not only at startup, so the endpoints refuse
/// on the production tier even if the feature flag is on.
/// </summary>
public class SmokeEndpointAvailableRequirementHandlerTests
{
    private const string Secret = "smoke-shared-secret-8f3c1d9a";

    [Theory]
    [InlineData("dev")]
    [InlineData("staging")]
    [InlineData("preprod")]
    public async Task HandleAsync_WhenEnabledOnANonProductionTier_Succeeds(string tier)
    {
        var (sut, context, _) = CreateSut(enabled: true, tier: tier);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenTheEndpointIsDisabled_DoesNotSucceed()
    {
        var (sut, context, _) = CreateSut(enabled: false, tier: "preprod");

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenTheTierIsProductionAndTheFlagIsOn_DoesNotSucceed()
    {
        var (sut, context, _) = CreateSut(enabled: true, tier: "prod");

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "the policy must refuse in prod even if the flag was copied in from another environment");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qa")]
    public async Task HandleAsync_WhenTheTierIsMissingOrUnrecognisedAndTheFlagIsOn_DoesNotSucceed(string? tier)
    {
        var (sut, context, _) = CreateSut(enabled: true, tier: tier);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("an absent or mistyped tier is not a permission");
    }

    [Fact]
    public async Task HandleAsync_WhenItDenies_AuditsTheDecisionWithoutTheSecret()
    {
        var (sut, context, logger) = CreateSut(enabled: true, tier: "prod");

        await sut.HandleAsync(context);

        logger.Invocations.Should().NotBeEmpty("every denial is audit-logged");
        logger.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString() ?? string.Empty)
            .Should().NotContain(text => text.Contains(Secret));
    }

    private static (SmokeEndpointAvailableRequirementHandler Sut,
        AuthorizationHandlerContext Context,
        Mock<ILogger<SmokeEndpointAvailableRequirementHandler>> Logger) CreateSut(
        bool enabled, string? tier)
    {
        var logger = new Mock<ILogger<SmokeEndpointAvailableRequirementHandler>>();
        var sut = new SmokeEndpointAvailableRequirementHandler(
            Options.Create(new IssueTokenEndpointOptions { Enabled = enabled, Secret = Secret }),
            Options.Create(new DeploymentOptions { Tier = tier }),
            logger.Object);

        var requirement = new SmokeEndpointAvailableRequirement();
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity(SmokeClientAuthenticationHandler.SchemeName)),
            resource: null);

        return (sut, context, logger);
    }
}
