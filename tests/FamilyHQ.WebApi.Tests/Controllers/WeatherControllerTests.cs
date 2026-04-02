using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class WeatherControllerTests
{
    [Fact]
    public async Task Refresh_CallsRefreshService_ReturnsOk()
    {
        // Arrange
        var weatherServiceMock = new Mock<IWeatherService>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        weatherRefreshServiceMock.Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new WeatherController(weatherServiceMock.Object, weatherRefreshServiceMock.Object);

        // Act
        var result = await sut.Refresh(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
