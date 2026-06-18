namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WeatherRefreshServiceTests
{
    private static WeatherResponse BuildMinimalResponse(List<WeatherDailyItem> daily) =>
        new(
            CurrentCondition: WeatherCondition.Clear,
            CurrentTemperatureCelsius: 15,
            CurrentWindSpeedKmh: 5,
            HourlyForecasts: [],
            DailyForecasts: daily);

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
}
