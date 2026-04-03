using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class WeatherControllerTests
{
    [Fact]
    public async Task Refresh_WithAuthenticatedUser_CallsUserScopedRefresh()
    {
        // Arrange
        var weatherServiceMock = new Mock<IWeatherService>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns("test-user-123");
        weatherRefreshServiceMock.Setup(x => x.RefreshAsync("test-user-123", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new WeatherController(weatherServiceMock.Object, weatherRefreshServiceMock.Object, currentUserMock.Object);

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync("test-user-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_WithAnonymousUser_ReturnsUnauthorized()
    {
        // Arrange
        var weatherServiceMock = new Mock<IWeatherService>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns((string?)null);

        var sut = new WeatherController(weatherServiceMock.Object, weatherRefreshServiceMock.Object, currentUserMock.Object);

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
