using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class SettingsControllerTests
{
    private const string TestUserId = "test-user-123";

    [Fact]
    public async Task GetLocation_Returns404_WhenNotSet()
    {
        // Arrange
        var (sut, locationRepoMock, _, _, _, _, _, _) = CreateSut();
        locationRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        // Act
        var result = await sut.GetLocation(CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenSet()
    {
        // Arrange
        var (sut, locationRepoMock, _, _, _, _, _, _) = CreateSut();
        locationRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { PlaceName = "Edinburgh, Scotland", Latitude = 55.9, Longitude = -3.2 });

        // Act
        var result = await sut.GetLocation(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LocationSettingDto>()
            .Which.PlaceName.Should().Be("Edinburgh, Scotland");
    }

    [Fact]
    public async Task SaveLocation_Geocodes_SavesPersists_AndTriggers()
    {
        // Arrange
        var (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, _, weatherRefreshServiceMock) = CreateSut();
        weatherRefreshServiceMock.Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        geocodingMock.Setup(x => x.GeocodeAsync("Edinburgh, Scotland", It.IsAny<CancellationToken>()))
            .ReturnsAsync((55.9533, -3.1883));
        locationRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<LocationSetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, LocationSetting ls, CancellationToken _) => ls);
        dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(new DateOnly(2026, 6, 15),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30), "Daytime"));
        schedulerMock.Setup(x => x.TriggerRecalculationAsync()).Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        var clientMock = new Mock<IClientProxy>();
        clientsMock.Setup(x => x.All).Returns(clientMock.Object);
        hubMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        // Act
        var result = await sut.SaveLocation(new SaveLocationRequest("Edinburgh, Scotland"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        schedulerMock.Verify(x => x.TriggerRecalculationAsync(), Times.Once);
        clientMock.Verify(x => x.SendCoreAsync("ThemeChanged", It.Is<object[]>(o => o.Length > 0), It.IsAny<CancellationToken>()), Times.Once);
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        SettingsController sut,
        Mock<ILocationSettingRepository> locationRepoMock,
        Mock<IGeocodingService> geocodingMock,
        Mock<IDayThemeService> dayThemeServiceMock,
        Mock<IDayThemeScheduler> schedulerMock,
        Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> hubMock,
        Mock<IDisplaySettingRepository> displayRepoMock,
        Mock<IWeatherRefreshService> weatherRefreshServiceMock) CreateSut()
    {
        var locationRepoMock = new Mock<ILocationSettingRepository>();
        var geocodingMock = new Mock<IGeocodingService>();
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        var schedulerMock = new Mock<IDayThemeScheduler>();
        var hubMock = new Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>>();
        var loggerMock = new Mock<ILogger<SettingsController>>();
        var displayRepoMock = new Mock<IDisplaySettingRepository>();
        var weatherServiceMock = new Mock<IWeatherService>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns(TestUserId);

        var sut = new SettingsController(
            locationRepoMock.Object,
            geocodingMock.Object,
            dayThemeServiceMock.Object,
            schedulerMock.Object,
            hubMock.Object,
            loggerMock.Object,
            displayRepoMock.Object,
            weatherServiceMock.Object,
            weatherRefreshServiceMock.Object,
            currentUserMock.Object);

        return (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, displayRepoMock, weatherRefreshServiceMock);
    }
}
