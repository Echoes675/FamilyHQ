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

public class DisplaySettingsControllerTests
{
    [Fact]
    public async Task GetDisplay_ReturnsDefaults_WhenNotSet()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync("test-user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DisplaySetting?)null);

        // Act
        var result = await sut.GetDisplay(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DisplaySettingDto>().Subject;
        dto.SurfaceMultiplier.Should().Be(1.0);
        dto.OpaqueSurfaces.Should().BeFalse();
        dto.TransitionDurationSecs.Should().Be(15);
        dto.ThemeSelection.Should().Be("auto");
    }

    [Fact]
    public async Task GetDisplay_ReturnsSaved_WhenSet()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync("test-user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplaySetting
            {
                UserId = "test-user-123",
                SurfaceMultiplier = 0.8,
                OpaqueSurfaces = true,
                TransitionDurationSecs = 30,
                ThemeSelection = "evening"
            });

        // Act
        var result = await sut.GetDisplay(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DisplaySettingDto>().Subject;
        dto.SurfaceMultiplier.Should().Be(0.8);
        dto.OpaqueSurfaces.Should().BeTrue();
        dto.TransitionDurationSecs.Should().Be(30);
        dto.ThemeSelection.Should().Be("evening");
    }

    [Fact]
    public async Task PutDisplay_ValidDto_UpsertAndReturnsOk()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.UpsertAsync("test-user-123", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DisplaySetting ds, CancellationToken _) => ds);

        var dto = new DisplaySettingDto(0.8, false, 20, "morning");

        // Act
        var result = await sut.PutDisplay(dto, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DisplaySettingDto>();
        displayRepoMock.Verify(x => x.UpsertAsync(
            "test-user-123",
            It.Is<DisplaySetting>(ds =>
                ds.SurfaceMultiplier == 0.8 &&
                ds.TransitionDurationSecs == 20 &&
                !ds.OpaqueSurfaces &&
                ds.ThemeSelection == "morning"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutDisplay_InvalidDto_ReturnsBadRequest()
    {
        // Arrange
        var (sut, _, _, _, _, _, _) = CreateSut();
        var dto = new DisplaySettingDto(5.0, false, 100); // out of range

        // Act
        var result = await sut.PutDisplay(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static (
        SettingsController sut,
        Mock<ILocationSettingRepository> locationRepoMock,
        Mock<IGeocodingService> geocodingMock,
        Mock<IDayThemeService> dayThemeServiceMock,
        Mock<IDayThemeScheduler> schedulerMock,
        Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> hubMock,
        Mock<IDisplaySettingRepository> displayRepoMock) CreateSut()
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
        currentUserMock.Setup(x => x.UserId).Returns("test-user-123");

        var locationServiceMock = new Mock<ILocationService>();

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
            locationServiceMock.Object);

        return (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, displayRepoMock);
    }
}
