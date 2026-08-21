namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

public class WeatherRefreshServiceTests
{
    private const string UserId = "user-1";
    private static readonly DateTimeOffset RetrievedAt = new(2026, 6, 18, 8, 0, 0, TimeSpan.Zero);

    private static bool MissingSectionsAre(object? state, params WeatherDataType[] expected) =>
        state is IReadOnlyList<KeyValuePair<string, object?>> values
        && values.Any(kv => kv.Key == "MissingSections"
            && kv.Value is IReadOnlyList<WeatherDataType> sections
            && sections.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)));

    private static WeatherCurrentItem Current() =>
        new(WeatherCondition.Clear, TemperatureCelsius: 15, WindSpeedKmh: 5);

    private static WeatherHourlyItem Hour(int hour) =>
        new(new DateTimeOffset(2026, 6, 18, hour, 0, 0, TimeSpan.Zero),
            WeatherCondition.Clear, TemperatureCelsius: 14, WindSpeedKmh: 6);

    private static WeatherDailyItem Day(int day) =>
        new(new DateOnly(2026, 6, day), WeatherCondition.Clear,
            HighCelsius: 22, LowCelsius: 12, WindSpeedMaxKmh: 15);

    private static WeatherResponse BuildMinimalResponse(List<WeatherDailyItem> daily) =>
        new(Current: Current(), HourlyForecasts: [], DailyForecasts: daily);

    /// <summary>
    /// Builds the service over substituted collaborators. Returns the data-point repository so a
    /// test can read the rows the refresh actually handed it, and the logger so a test can read the
    /// production signal the refresh emitted.
    /// </summary>
    private static (WeatherRefreshService Sut,
                    Mock<IWeatherDataPointRepository> DataRepo,
                    Mock<ILogger<WeatherRefreshService>> Logger) CreateSut(
        WeatherResponse response, DateTimeOffset? now = null)
    {
        var settingRepo = new Mock<IWeatherSettingRepository>();
        settingRepo.Setup(x => x.GetOrCreateAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherSetting { Enabled = true, WindThresholdKmh = 30 });

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { Id = 1, UserId = UserId, Latitude = 53.35, Longitude = -6.26 });

        var provider = new Mock<IWeatherProvider>();
        provider.Setup(x => x.GetWeatherAsync(53.35, -6.26, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        var logger = new Mock<ILogger<WeatherRefreshService>>();

        var sut = new WeatherRefreshService(
            settingRepo.Object,
            locationRepo.Object,
            provider.Object,
            dataRepo.Object,
            new Mock<IWeatherBroadcaster>().Object,
            new Mock<ITimeZoneLookup>().Object,
            new FakeTimeProvider(now ?? RetrievedAt),
            logger.Object);

        return (sut, dataRepo, logger);
    }

    private static void VerifyDegradedLog(
        Mock<ILogger<WeatherRefreshService>> logger, Times times, params WeatherDataType[] missing) =>
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => MissingSectionsAre(v, missing)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public void BuildDataPoints_DailyTimestamp_WithBstZone_AnchoredToLocalMidnight()
    {
        // Daily record for June 18 — with Dublin BST (UTC+1), local midnight = UTC 23:00 June 17.
        // Expected Timestamp: 2026-06-17T23:00:00+01:00 (which DbContext stores as 2026-06-17T23:00Z).
        var response = BuildMinimalResponse([
            new WeatherDailyItem(
                new DateOnly(2026, 6, 18),
                WeatherCondition.Clear,
                HighCelsius: 22,
                LowCelsius: 12,
                WindSpeedMaxKmh: 15)
        ]);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1,
            response: response,
            retrievedAt: new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
            windThresholdKmh: 20,
            ianaTimeZone: "Europe/Dublin");

        var daily = dataPoints.Single(p => p.DataType == WeatherDataType.Daily);
        daily.Timestamp.Offset.Should().Be(TimeSpan.FromHours(1),
            "Europe/Dublin BST midnight for June 18 is 00:00+01:00");
        daily.Timestamp.UtcDateTime.Should().Be(new DateTime(2026, 6, 17, 23, 0, 0, DateTimeKind.Utc),
            "local midnight June 18 BST = UTC June 17 23:00");
    }

    [Fact]
    public void BuildDataPoints_DailyTimestamp_NullZone_UsesUtcMidnight()
    {
        var response = BuildMinimalResponse([
            new WeatherDailyItem(
                new DateOnly(2026, 6, 18),
                WeatherCondition.Clear,
                HighCelsius: 22,
                LowCelsius: 12,
                WindSpeedMaxKmh: 15)
        ]);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1,
            response: response,
            retrievedAt: new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
            windThresholdKmh: 20,
            ianaTimeZone: null);

        var daily = dataPoints.Single(p => p.DataType == WeatherDataType.Daily);
        daily.Timestamp.Offset.Should().Be(TimeSpan.Zero,
            "null zone falls back to UTC midnight (offset zero)");
        daily.Timestamp.UtcDateTime.Should().Be(new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void BuildDataPoints_DailyTimestamp_UnknownZone_FallsBackToUtcMidnight()
    {
        var response = BuildMinimalResponse([
            new WeatherDailyItem(new DateOnly(2026, 6, 18), WeatherCondition.Clear,
                HighCelsius: 22, LowCelsius: 12, WindSpeedMaxKmh: 15)
        ]);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1,
            response: response,
            retrievedAt: new DateTimeOffset(2026, 6, 18, 8, 0, 0, TimeSpan.Zero),
            windThresholdKmh: 20,
            ianaTimeZone: "Not/AZone");

        var daily = dataPoints.Single(p => p.DataType == WeatherDataType.Daily);
        daily.Timestamp.Offset.Should().Be(TimeSpan.Zero,
            "unrecognised zone falls back to UTC midnight");
        daily.Timestamp.UtcDateTime.Should().Be(new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc));
    }

    // FHQ-159. A response with no current block used to still produce a Current row carrying
    // Unknown / 0.0 °C / 0 km/h, so the kiosk showed invented numbers as a reading about NOW.
    // Writing no row leaves the previous reading standing until its retention window expires.
    [Fact]
    public void BuildDataPoints_ResponseWithoutCurrentBlock_WritesNoCurrentRow()
    {
        var response = new WeatherResponse(Current: null, HourlyForecasts: [Hour(9)], DailyForecasts: [Day(18)]);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1, response: response, retrievedAt: RetrievedAt,
            windThresholdKmh: 20, ianaTimeZone: null);

        dataPoints.Should().NotContain(p => p.DataType == WeatherDataType.Current,
            "a fabricated 0 °C / 0 km/h reading is worse than no reading");
        dataPoints.Should().Contain(p => p.DataType == WeatherDataType.Hourly);
        dataPoints.Should().Contain(p => p.DataType == WeatherDataType.Daily);
    }

    [Fact]
    public void BuildDataPoints_ResponseWithCurrentBlock_WritesTheReportedReading()
    {
        var response = new WeatherResponse(
            Current: new WeatherCurrentItem(WeatherCondition.HeavyRain, TemperatureCelsius: 8.5, WindSpeedKmh: 42),
            HourlyForecasts: [],
            DailyForecasts: []);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1, response: response, retrievedAt: RetrievedAt,
            windThresholdKmh: 30, ianaTimeZone: null);

        var current = dataPoints.Should().ContainSingle(p => p.DataType == WeatherDataType.Current).Subject;
        current.Condition.Should().Be(WeatherCondition.HeavyRain);
        current.TemperatureCelsius.Should().Be(8.5);
        current.WindSpeedKmh.Should().Be(42);
        current.IsWindy.Should().BeTrue("42 km/h is over the 30 km/h threshold");
        current.RetrievedAt.Should().Be(RetrievedAt);
    }

    // FHQ-159. The section a degraded response omitted must contribute no rows, because the
    // repository derives the sections it replaces from the rows it is handed — a row present for an
    // empty section is what would delete the stored data the kiosk was showing correctly.
    [Fact]
    public void BuildDataPoints_EmptyHourlySection_ContributesNoHourlyRows()
    {
        var response = new WeatherResponse(Current: Current(), HourlyForecasts: [], DailyForecasts: [Day(18)]);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1, response: response, retrievedAt: RetrievedAt,
            windThresholdKmh: 20, ianaTimeZone: null);

        dataPoints.Should().NotContain(p => p.DataType == WeatherDataType.Hourly);
        dataPoints.Select(p => p.DataType).Distinct()
            .Should().BeEquivalentTo([WeatherDataType.Current, WeatherDataType.Daily]);
    }

    [Fact]
    public void BuildDataPoints_EmptyDailySection_ContributesNoDailyRows()
    {
        var response = new WeatherResponse(Current: Current(), HourlyForecasts: [Hour(9)], DailyForecasts: []);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1, response: response, retrievedAt: RetrievedAt,
            windThresholdKmh: 20, ianaTimeZone: null);

        dataPoints.Should().NotContain(p => p.DataType == WeatherDataType.Daily);
        dataPoints.Select(p => p.DataType).Distinct()
            .Should().BeEquivalentTo([WeatherDataType.Current, WeatherDataType.Hourly]);
    }

    [Fact]
    public void BuildDataPoints_ResponseCarryingNothing_ContributesNoRowsAtAll()
    {
        var response = new WeatherResponse(Current: null, HourlyForecasts: [], DailyForecasts: []);

        var dataPoints = WeatherRefreshService.BuildDataPoints(
            locationSettingId: 1, response: response, retrievedAt: RetrievedAt,
            windThresholdKmh: 20, ianaTimeZone: null);

        dataPoints.Should().BeEmpty(
            "a wholly empty response replaces nothing, so every stored section survives it");
    }

    // ── FHQ-159: the production signal for a degraded-but-200 response ───────────────────────────
    //
    // A ragged Open-Meteo payload arrives as a well-formed 200, so WeatherPollerService counts it a
    // SUCCESS (only exceptions are failures) and the empty-section detail is Debug, which is off in
    // production. Without an Information-level record here, a section could age out of its
    // retention window and vanish from the kiosk with nothing in Seq to explain it.

    [Fact]
    public async Task RefreshAsync_ResponseMissingTheHourlySection_ReportsTheGapAtInformation()
    {
        var (sut, _, logger) = CreateSut(
            new WeatherResponse(Current: Current(), HourlyForecasts: [], DailyForecasts: [Day(18)]));

        await sut.RefreshAsync(UserId);

        VerifyDegradedLog(logger, Times.Once(), WeatherDataType.Hourly);
    }

    [Fact]
    public async Task RefreshAsync_ResponseCarryingNothing_NamesEverySectionAsMissing()
    {
        var (sut, _, logger) = CreateSut(
            new WeatherResponse(Current: null, HourlyForecasts: [], DailyForecasts: []));

        await sut.RefreshAsync(UserId);

        VerifyDegradedLog(logger, Times.Once(),
            WeatherDataType.Current, WeatherDataType.Hourly, WeatherDataType.Daily);
    }

    [Fact]
    public async Task RefreshAsync_HealthyResponse_ReportsNoGap()
    {
        var (sut, _, logger) = CreateSut(
            new WeatherResponse(Current: Current(), HourlyForecasts: [Hour(9)], DailyForecasts: [Day(18)]));

        await sut.RefreshAsync(UserId);

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("carried no")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "a complete response is not a degradation, so the kiosk's every-poll path stays quiet");
    }

    [Fact]
    public async Task RefreshAsync_StampsRetrievedAtFromTheInjectedClock()
    {
        // WeatherService measures both retention windows against RetrievedAt through its injected
        // TimeProvider. Writing it from the ambient DateTimeOffset.UtcNow left one end of the
        // window on a clock no test could move (FHQ-159).
        var now = new DateTimeOffset(2026, 6, 18, 15, 30, 0, TimeSpan.Zero);
        var (sut, dataRepo, _) = CreateSut(
            new WeatherResponse(Current: Current(), HourlyForecasts: [Hour(9)], DailyForecasts: []),
            now);

        await sut.RefreshAsync(UserId);

        dataRepo.Verify(x => x.ReplaceSectionsAsync(
            1,
            It.Is<List<WeatherDataPoint>>(points => points.Count > 0 && points.All(p => p.RetrievedAt == now)),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "every row a refresh writes is stamped with that refresh's clock reading");
    }
}
