using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class WeatherControllerTests
{
    private const string TestUserId = "test-user-123";

    [Fact]
    public async Task Refresh_WithAuthenticatedUser_CallsUserScopedRefresh()
    {
        // Arrange
        var (sut, weatherServiceMock, weatherRefreshServiceMock) = CreateSut(TestUserId);
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 7, DataPointsWritten: 42));
        weatherServiceMock
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentWeatherDto(WeatherCondition.Clear, 12.0, IsWindy: false, WindSpeedKmh: 5.0, IconName: "sun"));

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_WithAnonymousUser_ReturnsUnauthorized()
    {
        // Arrange
        var (sut, _, weatherRefreshServiceMock) = CreateSut(userId: null);

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_WhenServiceReportsNoLocation_ReturnsConflictWithUserId()
    {
        // Arrange — an explicit client-initiated refresh with no saved location
        // used to silently return 200.  That masked an intermittent race where
        // POST /api/settings/location failed to make the location visible to
        // a subsequent refresh call, causing /api/weather/current to return 204
        // for up to 30s and flaking the E2E weather scenarios.  We now surface
        // the skip as 409 Conflict so the client fails fast with a clear reason.
        //
        // The 409 body also includes the server-observed userId so a failing
        // E2E run can compare it against the JWT sub in localStorage and tell
        // us whether the two requests even resolved the same identity.
        var (sut, _, weatherRefreshServiceMock) = CreateSut(TestUserId);
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.SkippedNoLocation, LocationSettingId: null, DataPointsWritten: 0));

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        AssertHasUserIdProperty(conflict.Value, TestUserId);
    }

    [Fact]
    public async Task Refresh_WhenServiceReportsWeatherDisabled_ReturnsConflictWithUserId()
    {
        // Arrange — same diagnostic requirement as the no-location case:
        // surface the server-observed userId in the 409 body so an E2E
        // failure can identify which user the server saw.
        var (sut, _, weatherRefreshServiceMock) = CreateSut(TestUserId);
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.SkippedWeatherDisabled, LocationSettingId: null, DataPointsWritten: 0));

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        AssertHasUserIdProperty(conflict.Value, TestUserId);
    }

    private static void AssertHasUserIdProperty(object? body, string expectedUserId)
    {
        body.Should().NotBeNull();
        var userIdProperty = body!.GetType().GetProperty("userId");
        userIdProperty.Should().NotBeNull("the 409 body must expose a userId field for E2E diagnostics");
        var value = userIdProperty!.GetValue(body);
        value.Should().Be(expectedUserId);
    }

    [Fact]
    public async Task Refresh_WhenRefreshSucceedsButDataNotVisible_ReturnsServiceUnavailable()
    {
        // Arrange — this is the exact intermittent failure mode we are guarding
        // against.  RefreshAsync reports success, but a follow-up read of
        // /current returns null (no current data point).  Previously this
        // produced a misleading 200 on /refresh and a 204 on /current.  We now
        // verify visibility server-side and return 503 Service Unavailable so
        // the client sees a single clear failure instead of silently polling.
        var (sut, weatherServiceMock, weatherRefreshServiceMock) = CreateSut(TestUserId);
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 7, DataPointsWritten: 42));
        weatherServiceMock
            .Setup(x => x.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrentWeatherDto?)null);

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    private static (WeatherController sut, Mock<IWeatherService> weatherServiceMock, Mock<IWeatherRefreshService> weatherRefreshServiceMock) CreateSut(string? userId)
    {
        var weatherServiceMock = new Mock<IWeatherService>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns(userId);

        var sut = new WeatherController(weatherServiceMock.Object, weatherRefreshServiceMock.Object, currentUserMock.Object);
        return (sut, weatherServiceMock, weatherRefreshServiceMock);
    }
}
