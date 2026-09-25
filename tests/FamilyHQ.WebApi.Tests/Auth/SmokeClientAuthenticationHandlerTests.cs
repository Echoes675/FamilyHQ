using System.Security.Claims;
using System.Text.Encodings.Web;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Auth;

/// <summary>
/// FHQ-139. The shared-secret requirement lives in its OWN authentication scheme, separate from the
/// FamilyHQ JWT scheme, so the secret can never be mistaken for — or satisfy — a user login.
/// </summary>
public class SmokeClientAuthenticationHandlerTests
{
    private const string ConfiguredSecret = "smoke-shared-secret-8f3c1d9a";

    [Fact]
    public async Task AuthenticateAsync_WhenTheBearerSecretMatches_Succeeds()
    {
        var (sut, _) = await CreateSutAsync(authorization: $"Bearer {ConfiguredSecret}");

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheBearerSecretMatches_PrincipalCarriesNoSubjectClaim()
    {
        var (sut, _) = await CreateSutAsync(authorization: $"Bearer {ConfiguredSecret}");

        var result = await sut.AuthenticateAsync();

        result.Principal!.Claims.Should().NotContain(claim => claim.Type == "sub",
            "the smoke client is a machine caller, not a user — a sub claim would let the shared " +
            "secret resolve as a FamilyHQ identity through ICurrentUserService");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheBearerSecretDoesNotMatch_DoesNotSucceed()
    {
        var (sut, _) = await CreateSutAsync(authorization: "Bearer not-the-secret");

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAValidLookingUserJwtIsPresented_DoesNotSucceed()
    {
        // A FamilyHQ user JWT is not the shared secret, so it cannot satisfy this scheme — and the
        // PreprodSmokeAccess policy accepts no other scheme.
        var (sut, _) = await CreateSutAsync(
            authorization: "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyMSJ9.signature");

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheAuthorizationHeaderIsMissing_DoesNotSucceed()
    {
        var (sut, _) = await CreateSutAsync(authorization: null);

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheSchemeIsNotBearer_DoesNotSucceed()
    {
        var (sut, _) = await CreateSutAsync(authorization: $"Basic {ConfiguredSecret}");

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenMultipleAuthorizationHeadersArePresented_DoesNotSucceed()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = new[] { "Bearer wrong", $"Bearer {ConfiguredSecret}" };
        var (sut, _) = await CreateSutAsync(httpContext: httpContext);

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse(
            "an ambiguous multi-valued header is refused rather than guessed at");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuthenticateAsync_WhenNoSecretIsConfigured_DoesNotSucceed(string? configuredSecret)
    {
        var (sut, _) = await CreateSutAsync(
            authorization: "Bearer anything", configuredSecret: configuredSecret);

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse("an unconfigured secret must never authenticate anyone");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenNoSecretIsConfigured_DoesNotSucceedForAnEmptyPresentedSecret()
    {
        var (sut, _) = await CreateSutAsync(authorization: "Bearer ", configuredSecret: null);

        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse("empty must not equal empty here");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheSecretDoesNotMatch_LogsNeitherTheConfiguredNorThePresentedSecret()
    {
        const string presented = "presented-secret-value-d41d8cd9";
        var (sut, logger) = await CreateSutAsync(authorization: $"Bearer {presented}");

        await sut.AuthenticateAsync();

        LoggedText(logger).Should().NotContain(text => text.Contains(presented));
        LoggedText(logger).Should().NotContain(text => text.Contains(ConfiguredSecret));
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheSecretDoesNotMatch_AuditsTheRejection()
    {
        var (sut, logger) = await CreateSutAsync(authorization: "Bearer not-the-secret");

        await sut.AuthenticateAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTheSecretMatches_AuditsTheSuccessWithoutTheSecret()
    {
        var (sut, logger) = await CreateSutAsync(authorization: $"Bearer {ConfiguredSecret}");

        await sut.AuthenticateAsync();

        logger.Verify(
            l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        LoggedText(logger).Should().NotContain(text => text.Contains(ConfiguredSecret));
    }

    [Fact]
    public async Task ChallengeAsync_WhenTheCallerIsNotAuthenticated_Responds404()
    {
        var httpContext = new DefaultHttpContext();
        var (sut, _) = await CreateSutAsync(httpContext: httpContext);

        await sut.ChallengeAsync(properties: null);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound,
            "a failure must look exactly like an unknown route — no recon signal");
    }

    [Fact]
    public async Task ChallengeAsync_WhenTheCallerIsNotAuthenticated_SendsNoWwwAuthenticateHeader()
    {
        var httpContext = new DefaultHttpContext();
        var (sut, _) = await CreateSutAsync(httpContext: httpContext);

        await sut.ChallengeAsync(properties: null);

        httpContext.Response.Headers.WWWAuthenticate.Should().BeEmpty(
            "advertising a challenge scheme would announce that the endpoint exists");
    }

    [Fact]
    public async Task ForbidAsync_WhenAPolicyRequirementFails_Responds404()
    {
        var httpContext = new DefaultHttpContext();
        var (sut, _) = await CreateSutAsync(
            httpContext: httpContext, authorization: $"Bearer {ConfiguredSecret}");

        await sut.ForbidAsync(properties: null);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    private static IEnumerable<string> LoggedText(Mock<ILogger> logger) =>
        logger.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString() ?? string.Empty);

    private static async Task<(SmokeClientAuthenticationHandler Sut, Mock<ILogger> Logger)> CreateSutAsync(
        string? authorization = null,
        string? configuredSecret = ConfiguredSecret,
        DefaultHttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();
        if (authorization is not null)
            httpContext.Request.Headers.Authorization = authorization;

        var logger = new Mock<ILogger>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(factory => factory.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        var schemeOptions = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Setup(options => options.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var endpointOptions = Options.Create(new IssueTokenEndpointOptions
        {
            Enabled = true,
            Secret = configuredSecret
        });

        var sut = new SmokeClientAuthenticationHandler(
            schemeOptions.Object, loggerFactory.Object, UrlEncoder.Default, endpointOptions);

        await sut.InitializeAsync(
            new AuthenticationScheme(
                SmokeClientAuthenticationHandler.SchemeName,
                displayName: null,
                handlerType: typeof(SmokeClientAuthenticationHandler)),
            httpContext);

        return (sut, logger);
    }
}
