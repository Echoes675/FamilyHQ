using System.Reflection;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

/// <summary>
/// FHQ-139. Both smoke endpoints. Everything that is not a well-formed request for a known user
/// answers 404 — structurally identical to an unknown route — with two deliberate exceptions: a body
/// that names nobody is a 400, and a revoked Google grant is a distinct 409 the preflight turns into
/// "sign in to preprod again".
/// </summary>
public class PreprodSmokeTokenControllerTests
{
    private const string SmokeUserId = "smoke-account-google-sub";
    private const string MintedJwt = "minted.jwt.value";
    private const string GoogleAccessToken = "google-access-token-value";
    private static readonly DateTimeOffset TokenExpiry = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueToken_WhenTheUserHasAStoredToken_ReturnsTheMintedJwt()
    {
        var sut = CreateSut();

        var result = await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<IssueTokenResponse>()
            .Which.Token.Should().Be(MintedJwt);
    }

    [Fact]
    public async Task IssueToken_WhenTheUserHasAStoredToken_MintsThroughTheSharedJwtPolicy()
    {
        var jwtTokenService = new Mock<IJwtTokenService>();
        jwtTokenService
            .Setup(service => service.GenerateToken(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(MintedJwt);
        var sut = CreateSut(jwtTokenService: jwtTokenService);

        await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        jwtTokenService.Verify(
            service => service.GenerateToken(SmokeUserId, null, null),
            Times.Once,
            "the endpoint must not own a second claims/lifetime policy — IJwtTokenService is the one owner");
    }

    [Fact]
    public async Task IssueToken_WhenSuccessful_ForbidsCachingTheResponse()
    {
        var httpContext = new DefaultHttpContext();
        var sut = CreateSut(httpContext: httpContext);

        await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        httpContext.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    [Fact]
    public async Task IssueToken_WhenTheUserIsUnknown_ReturnsNotFound()
    {
        var sut = CreateSut(storedUserIds: []);

        var result = await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueToken_WhenTheUserIsUnknown_MintsNothing()
    {
        var jwtTokenService = new Mock<IJwtTokenService>();
        var sut = CreateSut(storedUserIds: [], jwtTokenService: jwtTokenService);

        await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        jwtTokenService.Verify(
            service => service.GenerateToken(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>()),
            Times.Never);
    }

    [Fact]
    public async Task IssueToken_WhenThereIsNoBody_ReturnsBadRequest()
    {
        var sut = CreateSut();

        var result = await sut.IssueToken(request: null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IssueToken_WhenTheBodyNamesNoUser_ReturnsBadRequest(string userId)
    {
        var sut = CreateSut();

        var result = await sut.IssueToken(new SmokeTokenRequest(userId), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IssueToken_WhenSuccessful_DoesNotLogTheMintedJwt()
    {
        var sut = CreateSut(out var logger);

        await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        LoggedText(logger).Should().NotContain(text => text.Contains(MintedJwt));
    }

    [Fact]
    public async Task IssueToken_WhenSuccessful_AuditsTheIssue()
    {
        var sut = CreateSut(out var logger);

        await sut.IssueToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        logger.Invocations.Should().NotBeEmpty("every issue is audit-logged");
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenTheGrantIsValid_ReturnsTheAccessTokenAndItsExpiry()
    {
        var sut = CreateSut();

        var result = await sut.IssueGoogleAccessToken(
            new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<IssueGoogleAccessTokenResponse>().Subject;
        body.AccessToken.Should().Be(GoogleAccessToken);
        body.ExpiresAt.Should().Be(TokenExpiry);
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenSuccessful_ForbidsCachingTheResponse()
    {
        var httpContext = new DefaultHttpContext();
        var sut = CreateSut(httpContext: httpContext);

        await sut.IssueGoogleAccessToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        httpContext.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenTheUserIsUnknown_ReturnsNotFound()
    {
        var sut = CreateSut(googleOutcome: SmokeGoogleAccessTokenResult.UnknownUser());

        var result = await sut.IssueGoogleAccessToken(
            new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenThereIsNoBody_ReturnsBadRequest()
    {
        var sut = CreateSut();

        var result = await sut.IssueGoogleAccessToken(request: null, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenTheGrantIsRevoked_ReturnsConflict()
    {
        var sut = CreateSut(googleOutcome: SmokeGoogleAccessTokenResult.ReauthRequired());

        var result = await sut.IssueGoogleAccessToken(
            new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>(
            "a revoked grant is a distinct, documented answer — not the 404 that everything else gets");
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenTheGrantIsRevoked_ReturnsTheDocumentedErrorCode()
    {
        var sut = CreateSut(googleOutcome: SmokeGoogleAccessTokenResult.ReauthRequired());

        var result = await sut.IssueGoogleAccessToken(
            new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().BeOfType<SmokeErrorResponse>()
            .Which.Error.Should().Be(SmokeErrorResponse.ReauthRequiredErrorCode,
                "the smoke preflight keys on this code to say \"sign in to preprod again\"");
    }

    [Fact]
    public async Task IssueGoogleAccessToken_WhenSuccessful_DoesNotLogTheAccessToken()
    {
        var sut = CreateSut(out var logger);

        await sut.IssueGoogleAccessToken(new SmokeTokenRequest(SmokeUserId), CancellationToken.None);

        LoggedText(logger).Should().NotContain(text => text.Contains(GoogleAccessToken));
    }

    [Fact]
    public void Controller_WhenInspected_IsGuardedByThePreprodSmokeAccessPolicyAtClassLevel()
    {
        var authorize = typeof(PreprodSmokeTokenController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToList();

        authorize.Should().ContainSingle()
            .Which.Policy.Should().Be(PreprodSmokeAccessPolicy.Name,
                "the guard sits on the controller so it cannot be forgotten on a new action");
    }

    [Fact]
    public void Controller_WhenInspected_DeclaresNoActionLevelAnonymousOrAlternativePolicy()
    {
        var actionAttributes = typeof(PreprodSmokeTokenController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes(inherit: true))
            .ToList();

        actionAttributes.Should().NotContain(attribute => attribute is IAllowAnonymous);
        actionAttributes.Should().NotContain(attribute => attribute is AuthorizeAttribute);
    }

    private static IEnumerable<string> LoggedText(Mock<ILogger<PreprodSmokeTokenController>> logger) =>
        logger.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString() ?? string.Empty);

    private static PreprodSmokeTokenController CreateSut(
        out Mock<ILogger<PreprodSmokeTokenController>> logger,
        string[]? storedUserIds = null,
        Mock<IJwtTokenService>? jwtTokenService = null,
        SmokeGoogleAccessTokenResult? googleOutcome = null,
        DefaultHttpContext? httpContext = null)
    {
        logger = new Mock<ILogger<PreprodSmokeTokenController>>();

        var tokenStore = new Mock<ITokenStore>();
        tokenStore
            .Setup(store => store.GetAllUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedUserIds ?? [SmokeUserId]);

        jwtTokenService ??= DefaultJwtTokenService();

        var googleAccessTokenService = new Mock<ISmokeGoogleAccessTokenService>();
        googleAccessTokenService
            .Setup(service => service.IssueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(googleOutcome
                ?? SmokeGoogleAccessTokenResult.Issued(GoogleAccessToken, TokenExpiry));

        return new PreprodSmokeTokenController(
            tokenStore.Object,
            jwtTokenService.Object,
            googleAccessTokenService.Object,
            logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            }
        };
    }

    private static PreprodSmokeTokenController CreateSut(
        string[]? storedUserIds = null,
        Mock<IJwtTokenService>? jwtTokenService = null,
        SmokeGoogleAccessTokenResult? googleOutcome = null,
        DefaultHttpContext? httpContext = null) =>
        CreateSut(out _, storedUserIds, jwtTokenService, googleOutcome, httpContext);

    private static Mock<IJwtTokenService> DefaultJwtTokenService()
    {
        var jwtTokenService = new Mock<IJwtTokenService>();
        jwtTokenService
            .Setup(service => service.GenerateToken(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(MintedJwt);
        return jwtTokenService;
    }
}
