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
    public async Task GetLocation_WhenNoSavedLocation_Returns404_AndDoesNotInventOne()
    {
        // FHQ-179. The only automatic source was an ip-api call made from THIS container, so it
        // resolved the hosting VPS — a family in Derry with no saved location was shown a German
        // city labelled "Auto", as though FamilyHQ had worked out where they were. There is no
        // correct value to substitute, so the honest answer is "none": the client renders an empty
        // state rather than a confident wrong one.
        var (sut, locationRepoMock, _, _, _, _, _, _, _) = CreateSut();
        locationRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var result = await sut.GetLocation(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenSet()
    {
        // Arrange
        var (sut, locationRepoMock, _, _, _, _, _, _, _) = CreateSut();
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
        var (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, _, weatherRefreshServiceMock, timeZoneServiceMock) = CreateSut();
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 1, DataPointsWritten: 5));

        geocodingMock.Setup(x => x.GeocodeAsync("Edinburgh, Scotland", It.IsAny<CancellationToken>()))
            .ReturnsAsync((55.9533, -3.1883));
        locationRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<LocationSetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, LocationSetting ls, CancellationToken _) => ls);
        dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(new DateOnly(2026, 6, 15),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30),
                null,
                "Daytime"));
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
        // FHQ-177: the signal carries NO period. Asserting the payload is empty is the point —
        // a period here would be this kiosk's, pushed to every other kiosk.
        clientMock.Verify(x => x.SendCoreAsync("ThemeChanged", It.Is<object[]>(o => o.Length == 0), It.IsAny<CancellationToken>()), Times.Once);
        weatherRefreshServiceMock.Verify(x => x.RefreshAsync(TestUserId, It.IsAny<CancellationToken>()), Times.Once);
        timeZoneServiceMock.Verify(x => x.RepersistAutoIfNotExplicitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteLocation_TriggersWeatherRefresh()
    {
        // Arrange
        var (sut, locationRepoMock, _, dayThemeServiceMock, schedulerMock, hubMock, _, weatherRefreshServiceMock, timeZoneServiceMock) = CreateSut();
        locationRepoMock.Setup(x => x.DeleteAsync(TestUserId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(new DateOnly(2026, 6, 15),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30),
                null,
                "Daytime"));
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
        timeZoneServiceMock.Verify(x => x.RepersistAutoIfNotExplicitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTimeZone_WhenExplicit_ReturnsZoneAndIsExplicit_FromPersistedState()
    {
        // Arrange — persisted explicit zone (auto-detected = false).
        var (sut, _, _, _, _, _, displayRepoMock, _, timeZoneServiceMock) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplaySetting { UserId = TestUserId, IanaTimeZone = "Europe/London", IsTimeZoneAutoDetected = false });

        // Act
        var result = await sut.GetTimeZone(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TimeZoneSettingDto>().Subject;
        dto.EffectiveIanaZone.Should().Be("Europe/London");
        dto.IsExplicit.Should().BeTrue();
        dto.ExplicitIanaZone.Should().Be("Europe/London");
        // The display path must never trigger a live resolve.
        timeZoneServiceMock.Verify(x => x.ResolveAutoZoneAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTimeZone_WhenAutoDetected_ReturnsZoneButNotExplicit()
    {
        // Arrange — persisted auto-detected zone.
        var (sut, _, _, _, _, _, displayRepoMock, _, _) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplaySetting { UserId = TestUserId, IanaTimeZone = "Europe/Berlin", IsTimeZoneAutoDetected = true });

        // Act
        var result = await sut.GetTimeZone(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TimeZoneSettingDto>().Subject;
        dto.EffectiveIanaZone.Should().Be("Europe/Berlin");
        dto.IsExplicit.Should().BeFalse();
        dto.ExplicitIanaZone.Should().BeNull();
    }

    [Fact]
    public async Task GetTimeZone_WhenUnset_FallsBackToUtc()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock, _, _) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DisplaySetting?)null);

        // Act
        var result = await sut.GetTimeZone(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<TimeZoneSettingDto>().Subject;
        dto.EffectiveIanaZone.Should().Be("UTC");
        dto.IsExplicit.Should().BeFalse();
        dto.ExplicitIanaZone.Should().BeNull();
    }

    [Fact]
    public async Task PutDisplay_WhenUserHasExplicitTimeZone_PreservesIanaTimeZone()
    {
        // Arrange
        var (sut, _, _, _, _, _, displayRepoMock, _, _) = CreateSut();
        var existingSetting = new DisplaySetting
        {
            UserId = TestUserId,
            SurfaceMultiplier = 1.0,
            OpaqueSurfaces = false,
            TransitionDurationSecs = 15,
            ThemeSelection = "auto",
            IanaTimeZone = "America/New_York"
        };
        displayRepoMock.Setup(x => x.GetAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSetting);
        DisplaySetting? upsertedSetting = null;
        displayRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
            .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upsertedSetting = s)
            .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        var dto = new DisplaySettingDto(0.8, true, 20, "evening");

        // Act
        var result = await sut.PutDisplay(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        upsertedSetting.Should().NotBeNull();
        upsertedSetting!.IanaTimeZone.Should().Be("America/New_York",
            "display settings must not wipe an explicitly-set IANA timezone");
        upsertedSetting.SurfaceMultiplier.Should().Be(0.8);
        upsertedSetting.ThemeSelection.Should().Be("evening");
    }

    [Fact]
    public async Task SetTimeZone_WithInvalidZone_ReturnsBadRequest()
    {
        // Arrange
        var (sut, _, _, _, _, _, _, _, timeZoneServiceMock) = CreateSut();
        timeZoneServiceMock.Setup(x => x.IsValidZone("Not/A/Zone")).Returns(false);

        // Act
        var result = await sut.SetTimeZone(new SetTimeZoneRequest("Not/A/Zone"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetTimeZone_WithValidZone_CallsSetExplicit_AndReturnsNoContent()
    {
        // Arrange
        var (sut, _, _, _, _, _, _, _, timeZoneServiceMock) = CreateSut();
        timeZoneServiceMock.Setup(x => x.IsValidZone("Europe/London")).Returns(true);
        timeZoneServiceMock.Setup(x => x.SetExplicitZoneAsync("Europe/London", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.SetTimeZone(new SetTimeZoneRequest("Europe/London"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        timeZoneServiceMock.Verify(
            x => x.SetExplicitZoneAsync("Europe/London", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetTimeZone_CallsResetToAuto_AndReturnsNoContent()
    {
        // Arrange
        var (sut, _, _, _, _, _, _, _, timeZoneServiceMock) = CreateSut();
        timeZoneServiceMock.Setup(x => x.ResetToAutoZoneAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await sut.ResetTimeZone(CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        timeZoneServiceMock.Verify(
            x => x.ResetToAutoZoneAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // FHQ-166. SaveLocation's diagnostic line exists to confirm that the save and the subsequent
    // weather refresh resolved the same identity from the JWT — the user id is the whole point of
    // the comparison. It used to also carry the place name, which is the family's home address.
    [Fact]
    public async Task SaveLocation_DiagnosticLog_CarriesTheUserIdButNotThePlaceName()
    {
        const string placeName = "Sentinelford, Nowhereshire";
        var (sut, geocodingMock, loggerMock) = CreateSutExposingItsLogger();

        geocodingMock.Setup(x => x.GeocodeAsync(placeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((55.9533, -3.1883));

        await sut.SaveLocation(new SaveLocationRequest(placeName), CancellationToken.None);

        VerifyLogged(loggerMock, TestUserId, Times.AtLeastOnce(),
            "the identity comparison this line exists for still needs the user id");
        VerifyLogged(loggerMock, placeName, Times.Never(),
            "the place name is the family's home address and must never reach a log sink");
        VerifyLogged(loggerMock, "Sentinelford", Times.Never(),
            "not even part of the address may survive");
    }

    private static void VerifyLogged(
        Mock<ILogger<SettingsController>> logger, string fragment, Times times, string because) =>
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, fragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times,
            because);

    /// <summary>
    /// True when the log record carries <paramref name="fragment"/> in its rendered message or in
    /// any structured property — a property reaches Seq as its own field even when the message
    /// template does not show it.
    /// </summary>
    private static bool Carries(object? state, string fragment)
    {
        if (state?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return state is IReadOnlyList<KeyValuePair<string, object?>> values
            && values.Any(kv => kv.Value?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// A SaveLocation-shaped SUT that hands back its logger. The main <see cref="CreateSut"/> tuple
    /// is already at ten members, so a redaction assertion gets its own narrow builder rather than
    /// widening it to eleven for every caller.
    /// </summary>
    private static (
        SettingsController Sut,
        Mock<IGeocodingService> GeocodingMock,
        Mock<ILogger<SettingsController>> LoggerMock) CreateSutExposingItsLogger()
    {
        var locationRepoMock = new Mock<ILocationSettingRepository>();
        var geocodingMock = new Mock<IGeocodingService>();
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        var schedulerMock = new Mock<IDayThemeScheduler>();
        var hubMock = new Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>>();
        var loggerMock = new Mock<ILogger<SettingsController>>();
        var weatherRefreshServiceMock = new Mock<IWeatherRefreshService>();
        var currentUserMock = new Mock<ICurrentUserService>();

        currentUserMock.Setup(x => x.UserId).Returns(TestUserId);
        locationRepoMock.Setup(x => x.UpsertAsync(TestUserId, It.IsAny<LocationSetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, LocationSetting ls, CancellationToken _) => ls);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(new DateOnly(2026, 6, 15),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30),
                null, "Daytime"));
        weatherRefreshServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, LocationSettingId: 1, DataPointsWritten: 5));

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(x => x.All).Returns(new Mock<IClientProxy>().Object);
        hubMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        var sut = new SettingsController(
            locationRepoMock.Object,
            geocodingMock.Object,
            dayThemeServiceMock.Object,
            schedulerMock.Object,
            hubMock.Object,
            loggerMock.Object,
            new Mock<IDisplaySettingRepository>().Object,
            new Mock<IWeatherService>().Object,
            weatherRefreshServiceMock.Object,
            currentUserMock.Object,
            new Mock<ITimeZoneService>().Object);

        return (sut, geocodingMock, loggerMock);
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
                        timeZoneServiceMock.Object);

        return (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, displayRepoMock, weatherRefreshServiceMock, timeZoneServiceMock);
    }
}
