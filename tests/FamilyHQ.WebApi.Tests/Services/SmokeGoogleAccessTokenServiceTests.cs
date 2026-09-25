using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

/// <summary>
/// FHQ-139. The smoke suite's Google oracle: it refreshes the smoke account's STORED refresh token
/// through the existing refresh path, so that re-signing-in on preprod is the only upkeep — whatever
/// grant preprod holds is the one the tests use.
/// </summary>
public class SmokeGoogleAccessTokenServiceTests
{
    private const string SmokeUserId = "smoke-account-google-sub";
    private const string StoredRefreshToken = "stored-refresh-token-value";
    private const string GoogleAccessToken = "google-access-token-value";
    private static readonly DateTimeOffset TestNow = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueAsync_WhenTheGrantIsValid_ReturnsTheAccessTokenFromTheRefreshPath()
    {
        var sut = CreateSut(out _, out _);

        var result = await sut.IssueAsync(SmokeUserId);

        result.Outcome.Should().Be(SmokeGoogleAccessTokenOutcome.Issued);
        result.AccessToken.Should().Be(GoogleAccessToken);
    }

    [Fact]
    public async Task IssueAsync_WhenTheGrantIsValid_ExpiresAtIsNowPlusGoogleExpiresIn()
    {
        var sut = CreateSut(out _, out _, expiresInSeconds: 3600);

        var result = await sut.IssueAsync(SmokeUserId);

        result.ExpiresAt.Should().Be(TestNow.AddSeconds(3600));
    }

    [Fact]
    public async Task IssueAsync_WhenTheGrantIsValid_RefreshesTheStoredTokenForThatUser()
    {
        var sut = CreateSut(out _, out var refresher);

        await sut.IssueAsync(SmokeUserId);

        refresher.Verify(
            r => r.RefreshAccessTokenAsync(StoredRefreshToken, It.IsAny<CancellationToken>()),
            Times.Once,
            "the endpoint must reuse the existing refresh path, never a second implementation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task IssueAsync_WhenNoRefreshTokenIsStoredForTheUser_ReturnsUnknownUser(string? stored)
    {
        var sut = CreateSut(out _, out var refresher, storedRefreshToken: stored);

        var result = await sut.IssueAsync(SmokeUserId);

        result.Outcome.Should().Be(SmokeGoogleAccessTokenOutcome.UnknownUser);
        refresher.Verify(
            r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IssueAsync_WhenGoogleReportsTheGrantRevoked_ReturnsReauthRequired()
    {
        var sut = CreateSut(out _, out var refresher);
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked."));

        var result = await sut.IssueAsync(SmokeUserId);

        result.Outcome.Should().Be(SmokeGoogleAccessTokenOutcome.ReauthRequired);
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_WhenTheRefreshFailsForAnyOtherReason_PropagatesTheFailure()
    {
        var sut = CreateSut(out _, out var refresher);
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to refresh token. Status: ServiceUnavailable."));

        var act = () => sut.IssueAsync(SmokeUserId);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an unexpected Google failure is not a revoked grant and must not be reported as one");
    }

    [Fact]
    public async Task IssueAsync_WhileRefreshing_MakesTheUserAmbientSoARotatedRefreshTokenCanBePersisted()
    {
        string? ambientUserDuringRefresh = null;
        var sut = CreateSut(out _, out var refresher);
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => ambientUserDuringRefresh = BackgroundUserContext.Current)
            .ReturnsAsync((GoogleAccessToken, 3600));

        await sut.IssueAsync(SmokeUserId);

        ambientUserDuringRefresh.Should().Be(SmokeUserId,
            "Google may rotate the refresh token on a refresh grant, and the rotated value is saved "
            + "through the current-user token store overload — with no ambient user that save throws "
            + "and the stored preprod grant goes stale");
    }

    [Fact]
    public async Task IssueAsync_WhenComplete_ClearsTheAmbientUser()
    {
        var sut = CreateSut(out _, out _);

        await sut.IssueAsync(SmokeUserId);

        BackgroundUserContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_WhenTheRefreshThrows_StillClearsTheAmbientUser()
    {
        var sut = CreateSut(out _, out var refresher);
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        try { await sut.IssueAsync(SmokeUserId); } catch (InvalidOperationException) { /* expected */ }

        BackgroundUserContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_WhenTheGrantIsValid_LogsNeitherTheAccessTokenNorTheRefreshToken()
    {
        var sut = CreateSut(out var logger, out _);

        await sut.IssueAsync(SmokeUserId);

        LoggedText(logger).Should().NotContain(text => text.Contains(GoogleAccessToken));
        LoggedText(logger).Should().NotContain(text => text.Contains(StoredRefreshToken));
    }

    [Fact]
    public async Task IssueAsync_WhenTheGrantIsRevoked_LogsTheReauthNeedAtInformationWithoutTheRefreshToken()
    {
        var sut = CreateSut(out var logger, out var refresher);
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked."));

        await sut.IssueAsync(SmokeUserId);

        logger.Verify(
            l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            "a grant needing re-consent is an expected, handled condition — Information, not Error");
        LoggedText(logger).Should().NotContain(text => text.Contains(StoredRefreshToken));
    }

    private static IEnumerable<string> LoggedText(Mock<ILogger<SmokeGoogleAccessTokenService>> logger) =>
        logger.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString() ?? string.Empty);

    private static SmokeGoogleAccessTokenService CreateSut(
        out Mock<ILogger<SmokeGoogleAccessTokenService>> logger,
        out Mock<IGoogleTokenRefresher> refresher,
        string? storedRefreshToken = StoredRefreshToken,
        int expiresInSeconds = 3600)
    {
        // AsyncLocal state leaks across tests in the same execution context if a previous test left it
        // set; every test here starts from "no ambient user", as a real request does.
        BackgroundUserContext.Current = null;

        var tokenStore = new Mock<ITokenStore>();
        tokenStore
            .Setup(store => store.GetRefreshTokenAsync(SmokeUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedRefreshToken);

        refresher = new Mock<IGoogleTokenRefresher>();
        refresher
            .Setup(r => r.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleAccessToken, expiresInSeconds));

        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(provider => provider.GetUtcNow()).Returns(TestNow);

        logger = new Mock<ILogger<SmokeGoogleAccessTokenService>>();

        return new SmokeGoogleAccessTokenService(
            tokenStore.Object, refresher.Object, timeProvider.Object, logger.Object);
    }
}
