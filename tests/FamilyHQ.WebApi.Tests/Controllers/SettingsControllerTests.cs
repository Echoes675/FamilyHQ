using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
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
    public async Task GetLocation_WhenNoSavedLocation_ReturnsAutoDetectedLocation()
    {
        // Arrange
        var (sut, locationRepoMock, _, _, _, _, _, _, locationServiceMock, _) = CreateSut();
        locationRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);
        locationServiceMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FamilyHQ.Core.DTOs.LocationResult("Belfast, Northern Ireland, UK", 54.5, -5.9, IsAutoDetected: true, IanaTimeZone: null));

        // Act
        var result = await sut.GetLocation(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<LocationSettingDto>().Subject;
        dto.PlaceName.Should().Be("Belfast, Northern Ireland, UK");
        dto.IsAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenSet()
    {
        // Arrange
        var (sut, locationRepoMock, _, _, _, _, _, _, _, _) = CreateSut();
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
        var (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, _, weatherRefreshServiceMock, _, _) = CreateSut();
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 1, DataPointsWritten: 5));

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

    [Fact]
    public async Task DeleteLocation_TriggersWeatherRefresh()
    {
        // Arrange
        var (sut, locationRepoMock, _, dayThemeServiceMock, schedulerMock, hubMock, _, weatherRefreshServiceMock, locationServiceMock, _) = CreateSut();
        locationRepoMock.Setup(x => x.DeleteAsync(TestUserId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(new DateOnly(2026, 6, 15),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30), "Daytime"));
        schedulerMock.Setup(x => x.TriggerRecalculationAsync()).Returns(Task.CompletedTask);
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 1, DataPointsWritten: 5));

        var clientsMock = new Mock<IHubClients>();
        var clientMock = new Mock<IClientProxy>();
        clientsMock.Setup(x => x.All).Returns(clientMock.Object);
        hubMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        // Act
        await sut.DeleteLocation(CancellationToken.None);

        // Assert
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTimeZone_WithInvalidZone_ReturnsBadRequest()
    {
        // Arrange
        var (sut, _, _, _, _, _, _, _, _, timeZoneServiceMock) = CreateSut();
        timeZoneServiceMock.Setup(x => x.IsValidZone("Not/A/Zone")).Returns(false);

        // Act
        var result = await sut.SetTimeZone(new SetTimeZoneRequest("Not/A/Zone"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetTimeZone_WithValidZone_UpsertsSetting_AndReturnsNoContent()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock, _, _, timeZoneServiceMock) = CreateSut();
        timeZoneServiceMock.Setup(x => x.IsValidZone("Europe/London")).Returns(true);
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DisplaySetting?)null);
        displayRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        // Act
        var result = await sut.SetTimeZone(new SetTimeZoneRequest("Europe/London"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        displayRepoMock.Verify(
            x => x.UpsertAsync(TestUserId, It.Is<DisplaySetting>(s => s.IanaTimeZone == "Europe/London"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetTimeZone_ClearsExplicitZone_AndReturnsNoContent()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock, _, _, _) = CreateSut();
        DisplaySetting? upsertedSetting = null;
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplaySetting { UserId = TestUserId, IanaTimeZone = "Europe/London" });
        displayRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
            .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upsertedSetting = s)
            .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        // Act
        var result = await sut.ResetTimeZone(CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        displayRepoMock.Verify(
            x => x.UpsertAsync(TestUserId, It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()),
            Times.Once);
        upsertedSetting.Should().NotBeNull();
        upsertedSetting!.IanaTimeZone.Should().BeNull();
    }

    private static (
        SettingsController sut,
        Mock<ILocationSettingRepository> locationRepoMock,
        Mock<IGeocodingService> geocodingMock,
        Mock<IDayThemeService> dayThemeServiceMock,
        Mock<IDayThemeScheduler> schedulerMock,
        Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> hubMock,
        Mock<IDisplaySettingRepository> displayRepoMock,
        Mock<IWeatherRefreshService> weatherRefreshServiceMock,
        Mock<ILocationService> locationServiceMock,
        Mock<ITimeZoneService> timeZoneServiceMock) CreateSut()
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
        var locationServiceMock = new Mock<ILocationService>();
        var timeZoneServiceMock = new Mock<ITimeZoneService>();
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
            currentUserMock.Object,
            locationServiceMock.Object,
            timeZoneServiceMock.Object);

        return (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, displayRepoMock, weatherRefreshServiceMock, locationServiceMock, timeZoneServiceMock);
    }
}
