using System.Security.Claims;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Auth;

/// <summary>
/// FHQ-139. The smoke-account allowlist is the requirement that actually prevents token theft: even
/// with tier, flag and secret all wrong, these endpoints can only ever mint for the test account.
/// </summary>
public class SmokeAccountRequirementHandlerTests
{
    private const string SmokeUserId = "smoke-account-google-sub";
    private const string FamilyUserId = "family-member-google-sub";

    [Fact]
    public async Task HandleAsync_WhenTheRequestNamesTheSmokeAccount_Succeeds()
    {
        var (sut, context, _) = CreateSut(requestedUserId: SmokeUserId, configuredSmokeUserId: SmokeUserId);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenTheRequestNamesAnotherUser_DoesNotSucceed()
    {
        var (sut, context, _) = CreateSut(requestedUserId: FamilyUserId, configuredSmokeUserId: SmokeUserId);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse(
            "a family member's token must be unreachable through these endpoints");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WhenTheSmokeUserIdIsNotConfigured_DoesNotSucceed(string? configuredSmokeUserId)
    {
        var (sut, context, _) = CreateSut(
            requestedUserId: SmokeUserId, configuredSmokeUserId: configuredSmokeUserId);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("unset is a deny, never a wildcard");
    }

    [Fact]
    public async Task HandleAsync_WhenTheRequestCarriesNoUserId_DefersSoTheActionCanReturnBadRequest()
    {
        var (sut, context, _) = CreateSut(requestedUserId: null, configuredSmokeUserId: SmokeUserId);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue(
            "a request that names nobody can mint nothing; it is a malformed body, and the action " +
            "answers 400 for it");
    }

    [Fact]
    public async Task HandleAsync_WhenThereIsNoHttpContext_DoesNotSucceed()
    {
        var (sut, context, _) = CreateSut(
            requestedUserId: SmokeUserId,
            configuredSmokeUserId: SmokeUserId,
            httpContext: null,
            useDefaultHttpContext: false);

        await sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse("without a request there is nothing to allow");
    }

    [Fact]
    public async Task HandleAsync_WhenItDenies_AuditsTheDecisionWithoutTheRequestedUserIdBeingTrusted()
    {
        var (sut, context, logger) = CreateSut(
            requestedUserId: FamilyUserId, configuredSmokeUserId: SmokeUserId);

        await sut.HandleAsync(context);

        logger.Invocations.Should().NotBeEmpty("every denial is audit-logged");
    }

    private static (SmokeAccountRequirementHandler Sut,
        AuthorizationHandlerContext Context,
        Mock<ILogger<SmokeAccountRequirementHandler>> Logger) CreateSut(
        string? requestedUserId,
        string? configuredSmokeUserId,
        HttpContext? httpContext = null,
        bool useDefaultHttpContext = true)
    {
        if (httpContext is null && useDefaultHttpContext)
            httpContext = new DefaultHttpContext();

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.SetupGet(accessor => accessor.HttpContext).Returns(httpContext);

        var reader = new Mock<ISmokeRequestUserIdReader>();
        reader
            .Setup(r => r.ReadUserIdAsync(It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(requestedUserId);

        var logger = new Mock<ILogger<SmokeAccountRequirementHandler>>();
        var sut = new SmokeAccountRequirementHandler(
            httpContextAccessor.Object,
            reader.Object,
            Options.Create(new SmokeOptions { UserId = configuredSmokeUserId }),
            logger.Object);

        var context = new AuthorizationHandlerContext(
            [new SmokeAccountRequirement()],
            new ClaimsPrincipal(new ClaimsIdentity(SmokeClientAuthenticationHandler.SchemeName)),
            resource: null);

        return (sut, context, logger);
    }
}
