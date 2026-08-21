namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

public class WeatherServiceTests
{
    /// <summary>Fixed "now" for every staleness test — nothing here reads the wall clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Reads a structured log property rather than the rendered message.</summary>
    private static bool PropertyIs(object? state, string name, object expected) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> values
        && values.Any(kv => kv.Key == name && Equals(kv.Value, expected));

    private static WeatherService CreateSut(
        Mock<IWeatherDataPointRepository> dataRepo,
        Mock<ILocationSettingRepository> locationRepo,
        Mock<ITimeZoneLookup> tzLookup,
        string userId = "user-1",
        WeatherOptions? options = null,
        TimeProvider? timeProvider = null,
        Mock<ILogger<WeatherService>>? logger = null)
    {
        var weatherSettingRepoMock = new Mock<IWeatherSettingRepository>();
        weatherSettingRepoMock
            .Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherSetting { TemperatureUnit = TemperatureUnit.Celsius });

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns(userId);

        return new WeatherService(
            dataRepo.Object,
            weatherSettingRepoMock.Object,
            locationRepo.Object,
            currentUserMock.Object,
            tzLookup.Object,
            Microsoft.Extensions.Options.Options.Create(options ?? new WeatherOptions()),
            timeProvider ?? new FakeTimeProvider(Now),
            logger?.Object ?? NullLogger<WeatherService>.Instance);
    }

    private static Mock<ILocationSettingRepository> LocationRepoReturningDublin() =>
        MockLocationRepo(new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 });

    private static Mock<ILocationSettingRepository> MockLocationRepo(LocationSetting? location)
    {
        var mock = new Mock<ILocationSettingRepository>();
        mock.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(location);
        return mock;
    }

    private static Mock<ITimeZoneLookup> DublinZoneLookup()
    {
        var mock = new Mock<ITimeZoneLookup>();
        mock.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");
        return mock;
    }

    private static WeatherDataPoint Point(WeatherDataType type, DateTimeOffset retrievedAt) =>
        new()
        {
            LocationSettingId = 1,
            DataType = type,
            Timestamp = retrievedAt,
            RetrievedAt = retrievedAt,
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 20,
            HighCelsius = 25,
            LowCelsius = 15,
            WindSpeedKmh = 10,
            IsWindy = false
        };

    [Fact]
    public async Task GetDailyForecastAsync_ThreadsIanaZoneFromLocationToRepository()
    {
        var location = new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 };

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var tzLookup = new Mock<ITimeZoneLookup>();
        tzLookup.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        await sut.GetDailyForecastAsync(days: 7);

        dataRepo.Verify(
            x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()),
            Times.Once,
            "timezone from location lat/long must be passed to GetDailyAsync");
    }

    [Fact]
    public async Task GetHourlyAsync_ThreadsIanaZoneFromLocationToRepository()
    {
        var location = new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 };
        var date = new DateOnly(2026, 6, 18);

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var tzLookup = new Mock<ITimeZoneLookup>();
        tzLookup.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, date, "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        await sut.GetHourlyAsync(date);

        dataRepo.Verify(
            x => x.GetHourlyAsync(1, date, "Europe/Dublin", It.IsAny<CancellationToken>()),
            Times.Once,
            "timezone from location lat/long must be passed to repository");
    }

    [Fact]
    public async Task GetDailyForecastAsync_BstLocation_MapsLocalDateNotUtcDate()
    {
        // Simulate a daily record for Dublin BST June 18.
        // After EF UTC conversion, stored as 2026-06-17T23:00Z (offset stripped).
        // MapToDailyDto must recover June 18, not June 17.
        var location = new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 };
        var storedTimestamp = new DateTimeOffset(2026, 6, 17, 23, 0, 0, TimeSpan.Zero); // UTC midnight BST June 18

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var tzLookup = new Mock<ITimeZoneLookup>();
        tzLookup.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WeatherDataPoint
            {
                LocationSettingId = 1,
                DataType = WeatherDataType.Daily,
                Timestamp = storedTimestamp,
                // RetrievedAt is when the REFRESH ran, not the forecast's own timestamp — a row for
                // a past-midnight slot can still come from a refresh minutes ago (FHQ-159).
                RetrievedAt = Now.AddMinutes(-5),
                Condition = WeatherCondition.Clear,
                TemperatureCelsius = 20,
                HighCelsius = 25,
                LowCelsius = 15,
                WindSpeedKmh = 10,
                IsWindy = false
            }]);

        var sut = CreateSut(dataRepo, locationRepo, tzLookup);
        var result = await sut.GetDailyForecastAsync(days: 7);

        result.Should().ContainSingle();
        result[0].Date.Should().Be(new DateOnly(2026, 6, 18),
            "BST midnight June 18 is stored as 2026-06-17T23:00Z but should map to June 18");
    }

    [Fact]
    public async Task GetHourlyAsync_NullLocation_ReturnsEmpty()
    {
        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        var tzLookup = new Mock<ITimeZoneLookup>();
        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        var result = await sut.GetHourlyAsync(new DateOnly(2026, 6, 18));

        result.Should().BeEmpty();
        dataRepo.Verify(x => x.GetHourlyAsync(
            It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── FHQ-159: retention windows ──────────────────────────────────────────────────────────────
    //
    // Signed off 2026-08-17: forecast sections are retained 6 h past RetrievedAt, the Current row
    // 1 h, and a section past its window is hidden outright — no stale marker, no "last updated Xh
    // ago". Stored rows survive a degraded refresh (that is the per-section replace); these windows
    // are the backstop that stops a SUSTAINED outage leaving something visibly wrong on the kiosk.

    [Fact]
    public async Task GetCurrentAsync_ReadingInsideTheOneHourWindow_IsShown()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddMinutes(-59)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetCurrentAsync();

        result.Should().NotBeNull("59 minutes is inside the 60-minute Current window");
    }

    [Fact]
    public async Task GetCurrentAsync_ReadingExactlyAtTheWindow_IsStillShown()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddMinutes(-60)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetCurrentAsync();

        result.Should().NotBeNull("the window is inclusive at its boundary — it clears PAST 60 minutes");
    }

    [Fact]
    public async Task GetCurrentAsync_ReadingPastTheOneHourWindow_IsHidden()
    {
        // The tighter window exists because, unlike a forecast, this row asserts something about
        // NOW — an hours-old temperature is wrong rather than merely old.
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddMinutes(-61)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetCurrentAsync();

        result.Should().BeNull("61 minutes is past the 60-minute Current window, so the row is hidden entirely");
    }

    [Fact]
    public async Task GetCurrentAsync_ReadingAtFiveHours_IsHiddenEvenThoughAForecastWouldSurvive()
    {
        // Pins the two windows apart: 5 h is comfortably inside the 6 h forecast window and well
        // past the 1 h Current window, so a single shared window cannot satisfy this test.
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddHours(-5)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetCurrentAsync();

        result.Should().BeNull("the Current row does not get the forecast sections' 6-hour window");
    }

    [Fact]
    public async Task GetDailyForecastAsync_SectionInsideTheSixHourWindow_IsShown()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Daily, Now.AddMinutes(-359))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetDailyForecastAsync(days: 7);

        result.Should().ContainSingle(
            "5h59m is inside the 6-hour forecast window — 11 missed polls must not blank the kiosk");
    }

    [Fact]
    public async Task GetDailyForecastAsync_SectionExactlyAtTheSixHourWindow_IsStillShown()
    {
        // The boundary is the same "clears PAST the window" rule the Current row is pinned at, so
        // both windows are asserted at exactly their edge, not only either side of it.
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Daily, Now.AddMinutes(-360))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetDailyForecastAsync(days: 7);

        result.Should().ContainSingle("the window is inclusive at its boundary — it clears PAST 360 minutes");
    }

    [Fact]
    public async Task GetDailyForecastAsync_SectionPastTheSixHourWindow_IsHidden()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Daily, Now.AddMinutes(-361))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetDailyForecastAsync(days: 7);

        result.Should().BeEmpty("past 6 hours the section is hidden entirely, not marked as stale");
    }

    [Fact]
    public async Task GetHourlyAsync_SectionInsideTheSixHourWindow_IsShown()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, It.IsAny<DateOnly>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Hourly, Now.AddMinutes(-359))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetHourlyAsync(new DateOnly(2026, 6, 18));

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task GetHourlyAsync_SectionPastTheSixHourWindow_IsHidden()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, It.IsAny<DateOnly>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Hourly, Now.AddMinutes(-361))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetHourlyAsync(new DateOnly(2026, 6, 18));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHourlyAsync_MixedAges_HidesOnlyTheRowsPastTheWindow()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, It.IsAny<DateOnly>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Point(WeatherDataType.Hourly, Now.AddHours(-1)),
                Point(WeatherDataType.Hourly, Now.AddHours(-7))
            ]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup())
            .GetHourlyAsync(new DateOnly(2026, 6, 18));

        result.Should().ContainSingle("only the row from the 7-hour-old refresh is past the window");
    }

    [Fact]
    public async Task GetHourlyAsync_MixedAges_NamesTheNewestHiddenRefreshInTheLog()
    {
        // The message asserts the row it names is "past the retention window". Reporting the max
        // over EVERY row names the freshest one — which is not past the window at all — so an
        // operator reading Seq during an outage is told the section aged out at a time when it
        // demonstrably had not.
        var newestHidden = Now.AddHours(-7);
        var logger = new Mock<ILogger<WeatherService>>();
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, It.IsAny<DateOnly>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Point(WeatherDataType.Hourly, Now.AddHours(-1)),
                Point(WeatherDataType.Hourly, newestHidden),
                Point(WeatherDataType.Hourly, Now.AddHours(-9))
            ]);

        await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(), logger: logger)
            .GetHourlyAsync(new DateOnly(2026, 6, 18));

        logger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => PropertyIs(v, "RetrievedAt", newestHidden)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "the log must name the newest refresh that IS hidden, not the newest row present");
    }

    [Fact]
    public async Task GetHourlyAsync_EverySectionFresh_LogsNothing()
    {
        var logger = new Mock<ILogger<WeatherService>>();
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, It.IsAny<DateOnly>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Hourly, Now.AddHours(-1))]);

        await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(), logger: logger)
            .GetHourlyAsync(new DateOnly(2026, 6, 18));

        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "nothing was hidden, so a healthy dashboard read must be silent");
    }

    // Configurability is asserted in BOTH directions per window, so that a hard-coded constant that
    // happens to match one of them cannot pass: a widened window must show what the default hides,
    // and a narrowed one must hide what the default shows.
    [Fact]
    public async Task GetCurrentAsync_WidenedConfiguredWindow_ShowsWhatTheDefaultWouldHide()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddMinutes(-90)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(),
                options: new WeatherOptions { CurrentStaleAfterMinutes = 120 })
            .GetCurrentAsync();

        result.Should().NotBeNull("a 90-minute-old reading is hidden by the 60-minute default but not by a configured 120");
    }

    [Fact]
    public async Task GetCurrentAsync_NarrowedConfiguredWindow_HidesWhatTheDefaultWouldShow()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetCurrentAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Point(WeatherDataType.Current, Now.AddMinutes(-45)));

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(),
                options: new WeatherOptions { CurrentStaleAfterMinutes = 30 })
            .GetCurrentAsync();

        result.Should().BeNull("45 minutes survives the 60-minute default but not a configured 30");
    }

    [Fact]
    public async Task GetDailyForecastAsync_NarrowedConfiguredWindow_HidesWhatTheDefaultWouldShow()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Daily, Now.AddHours(-3))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(),
                options: new WeatherOptions { ForecastStaleAfterMinutes = 120 })
            .GetDailyForecastAsync(days: 7);

        result.Should().BeEmpty("a 3-hour-old section survives the 6-hour default but not a configured 2 hours");
    }

    [Fact]
    public async Task GetDailyForecastAsync_WidenedConfiguredWindow_ShowsWhatTheDefaultWouldHide()
    {
        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(WeatherDataType.Daily, Now.AddHours(-9))]);

        var result = await CreateSut(dataRepo, LocationRepoReturningDublin(), DublinZoneLookup(),
                options: new WeatherOptions { ForecastStaleAfterMinutes = 720 })
            .GetDailyForecastAsync(days: 7);

        result.Should().ContainSingle("a 9-hour-old section is hidden by the 6-hour default but not by a configured 12 hours");
    }
}
