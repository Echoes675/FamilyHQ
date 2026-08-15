namespace FamilyHQ.Services.Tests.Weather;

using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

public class OpenMeteoWeatherProviderTests
{
    private static Mock<ILogger<OpenMeteoWeatherProvider>> CreateLogger() => new();

    private static OpenMeteoWeatherProvider CreateProvider(
        FakeHttpHandler handler, Mock<ILogger<OpenMeteoWeatherProvider>>? logger = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        // The real mapper: the WMO-code → condition table is the behaviour under test here,
        // not a collaborator to be stubbed.
        return new OpenMeteoWeatherProvider(
            httpClient, new WmoCodeMapper(), (logger ?? CreateLogger()).Object);
    }

    [Fact]
    public async Task Parses_current_weather_from_api_response()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-04-01T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-04-01T12:00", "2026-04-01T13:00"],
                [14.5, 15.0],
                [3, 0],
                [22.0, 18.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-04-01", "2026-04-02"],
                [3, 0],
                [16.0, 18.0],
                [8.0, 9.0],
                [25.0, 12.0])));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.CurrentCondition.Should().Be(WeatherCondition.Cloudy);
        result.CurrentTemperatureCelsius.Should().Be(14.5);
        result.CurrentWindSpeedKmh.Should().Be(22.0);
        result.HourlyForecasts.Should().HaveCount(2);
        result.DailyForecasts.Should().HaveCount(2);
        result.DailyForecasts[0].HighCelsius.Should().Be(16.0);
        result.DailyForecasts[0].LowCelsius.Should().Be(8.0);
    }

    [Fact]
    public async Task GetWeatherAsync_HourlyTimestamp_WithBstZone_ConvertsToCorrectOffset()
    {
        // "2026-06-18T14:00" is local Dublin BST (UTC+1). Correct offset = +01:00, UTC = 13:00.
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-18T14:00", 20.0, 1, 10.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-18T14:00"],
                [20.0],
                [1],
                [10.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-18"],
                [1],
                [22.0],
                [12.0],
                [15.0])));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(53.35, -6.26, "Europe/Dublin");

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.FromHours(1),
            "Europe/Dublin in BST is UTC+1; the offset-less '14:00' should be treated as 14:00+01:00");
        hourlyTimestamp.UtcDateTime.Hour.Should().Be(13,
            "14:00 BST = 13:00 UTC");
    }

    // FHQ-121 pins: the pre-FHQ-107 hazard was DateTimeOffset.Parse(InvariantCulture)
    // applying the SERVER-LOCAL offset to Open-Meteo's offset-less timestamps. FHQ-107
    // replaced it with zone-aware NodaTime resolution (+ AssumeUniversal fallback).
    // The Tokyo (+09:00) pin plus the zero-offset fallback pins below cannot all be
    // satisfied by a host-local parse on ANY single host zone, so a regression fails
    // deterministically regardless of where the tests run.
    [Fact]
    public async Task GetWeatherAsync_HourlyTimestamp_WithTokyoZone_ProducesCorrectUtcInstant()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T14:00", 28.0, 0, 8.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T14:00"],
                [28.0],
                [0],
                [8.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-15"],
                [0],
                [30.0],
                [21.0],
                [12.0])));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(35.68, 139.69, "Asia/Tokyo");

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.FromHours(9),
            "Asia/Tokyo is fixed UTC+9; the offset-less '14:00' must be treated as 14:00+09:00");
        hourlyTimestamp.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 5, 0, 0, DateTimeKind.Utc),
            "14:00 JST = 05:00 UTC");
    }

    [Fact]
    public async Task GetWeatherAsync_HourlyTimestamp_UnknownZone_FallsBackToUtc()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T14:00", 20.0, 1, 10.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T14:00"],
                [20.0],
                [1],
                [10.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-15"],
                [1],
                [22.0],
                [12.0],
                [15.0])));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(53.35, -6.26, "Not/AZone");

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.Zero,
            "an unresolvable IANA zone falls back to AssumeUniversal, never the host-local offset");
        hourlyTimestamp.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetWeatherAsync_Request_AsksForAutoTimezone()
    {
        // API contract pin: timezone=auto makes Open-Meteo return offset-less timestamps
        // in the LOCATION's local zone. That is why ToLocalDateTimeOffset resolves them
        // against the caller-supplied IANA zone instead of blindly assuming UTC.
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T14:00", 20.0, 1, 10.0),
            Hourly: null,
            Daily: null));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        await provider.GetWeatherAsync(53.35, -6.26, "Europe/Dublin");

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.Query.Should().Contain("timezone=auto");
    }

    [Fact]
    public async Task GetWeatherAsync_HourlyTimestamp_NullZone_FallsBackToUtc()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-18T14:00", 20.0, 1, 10.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-18T14:00"],
                [20.0],
                [1],
                [10.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-18"],
                [1],
                [22.0],
                [12.0],
                [15.0])));

        var handler = new FakeHttpHandler(json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.Zero,
            "null zone falls back to AssumeUniversal — offset-less '14:00' treated as UTC");
        hourlyTimestamp.UtcDateTime.Hour.Should().Be(14);
    }

    // FHQ-110 pins: Open-Meteo's parallel value arrays are not guaranteed to be the
    // same length as `time`. Indexing them off `time.Count` threw
    // ArgumentOutOfRangeException, which lost the whole refresh (poller) or returned
    // 500 (POST /api/weather/refresh). Parse the safe overlap instead.
    [Fact]
    public async Task GetWeatherAsync_RaggedHourlyArrays_ReturnsEntriesUpToShortestArray()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00", "2026-06-15T13:00", "2026-06-15T14:00"],
                [14.5, 15.0],
                [3, 0, 1],
                [22.0, 18.0, 12.0]),
            Daily: null));

        var provider = CreateProvider(new FakeHttpHandler(json));

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.HourlyForecasts.Should().HaveCount(2,
            "temperature_2m is the shortest array at 2 entries; the third hour has no temperature");
        result.HourlyForecasts[0].TemperatureCelsius.Should().Be(14.5);
        result.HourlyForecasts[1].TemperatureCelsius.Should().Be(15.0);
    }

    [Fact]
    public async Task GetWeatherAsync_RaggedDailyArrays_ReturnsEntriesUpToShortestArray()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: null,
            Daily: new OpenMeteoDailyData(
                ["2026-06-15", "2026-06-16", "2026-06-17"],
                [3, 0, 1],
                [16.0, 18.0, 19.0],
                [8.0, 9.0, 10.0],
                [25.0])));

        var provider = CreateProvider(new FakeHttpHandler(json));

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.DailyForecasts.Should().HaveCount(1,
            "wind_speed_10m_max is the shortest array at 1 entry");
        result.DailyForecasts[0].Date.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task GetWeatherAsync_RaggedHourlyArrays_LogsWarningWithSectionAndCounts()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00", "2026-06-15T13:00", "2026-06-15T14:00"],
                [14.5, 15.0],
                [3, 0, 1],
                [22.0, 18.0, 12.0]),
            Daily: null));

        var logger = CreateLogger();
        var provider = CreateProvider(new FakeHttpHandler(json), logger);

        await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("hourly")
                && v.ToString()!.Contains("time=3")
                && v.ToString()!.Contains("temperature_2m=2")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "the degraded upstream response is logged once per parse, not once per element");
    }

    [Fact]
    public async Task GetWeatherAsync_RaggedDailyArrays_LogsWarningWithSectionAndCounts()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: null,
            Daily: new OpenMeteoDailyData(
                ["2026-06-15", "2026-06-16", "2026-06-17"],
                [3, 0, 1],
                [16.0, 18.0, 19.0],
                [8.0, 9.0, 10.0],
                [25.0])));

        var logger = CreateLogger();
        var provider = CreateProvider(new FakeHttpHandler(json), logger);

        await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("daily")
                && v.ToString()!.Contains("time=3")
                && v.ToString()!.Contains("wind_speed_10m_max=1")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetWeatherAsync_EvenLengthArrays_LogsNoWarning()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00", "2026-06-15T13:00"],
                [14.5, 15.0],
                [3, 0],
                [22.0, 18.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-15"],
                [3],
                [16.0],
                [8.0],
                [25.0])));

        var logger = CreateLogger();
        var provider = CreateProvider(new FakeHttpHandler(json), logger);

        await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "a healthy response must not produce warning noise on every poll");
    }

    [Fact]
    public async Task GetWeatherAsync_HourlyValueArrayMissingEntirely_ReturnsNoHourlyEntries()
    {
        // A missing value array deserialises to null; treat it as length 0 rather than
        // dereferencing it while walking `time`.
        const string json = """
            {
              "current": { "time": "2026-06-15T12:00", "temperature_2m": 14.5, "weather_code": 3, "wind_speed_10m": 22.0 },
              "hourly": { "time": ["2026-06-15T12:00", "2026-06-15T13:00"], "weather_code": [3, 0], "wind_speed_10m": [22.0, 18.0] }
            }
            """;

        var provider = CreateProvider(new FakeHttpHandler(json));

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.HourlyForecasts.Should().BeEmpty();
        result.CurrentTemperatureCelsius.Should().Be(14.5,
            "a degraded hourly block must not cost us the current conditions");
    }

    [Fact]
    public async Task GetWeatherAsync_HourlyTimeArrayMissingEntirely_ReturnsNoHourlyEntries()
    {
        const string json = """
            {
              "current": { "time": "2026-06-15T12:00", "temperature_2m": 14.5, "weather_code": 3, "wind_speed_10m": 22.0 },
              "hourly": { "temperature_2m": [14.5], "weather_code": [3], "wind_speed_10m": [22.0] }
            }
            """;

        var provider = CreateProvider(new FakeHttpHandler(json));

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.HourlyForecasts.Should().BeEmpty();
    }

    // FHQ-115 pins: an unmapped WMO code used to fall through to Clear, so unrecognised
    // severe weather rendered as sunny. It now surfaces as Unknown and is logged.
    [Fact]
    public async Task GetWeatherAsync_UnmappedWmoCode_MapsToUnknownCondition()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 4, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00"],
                [14.5],
                [50],
                [22.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-15"],
                [79],
                [16.0],
                [8.0],
                [25.0])));

        var provider = CreateProvider(new FakeHttpHandler(json));

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        result.CurrentCondition.Should().Be(WeatherCondition.Unknown);
        result.HourlyForecasts[0].Condition.Should().Be(WeatherCondition.Unknown);
        result.DailyForecasts[0].Condition.Should().Be(WeatherCondition.Unknown);
    }

    [Fact]
    public async Task GetWeatherAsync_UnmappedWmoCodes_LogsOneWarningNamingEveryCode()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 4, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00", "2026-06-15T13:00"],
                [14.5, 15.0],
                [50, 50],
                [22.0, 18.0]),
            Daily: null));

        var logger = CreateLogger();
        var provider = CreateProvider(new FakeHttpHandler(json), logger);

        await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains("4")
                && v.ToString()!.Contains("50")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "a 16-day forecast full of one unmapped code must not flood Seq — one warning per parse names every distinct code");
    }

    [Fact]
    public async Task GetWeatherAsync_AllWmoCodesMapped_LogsNoUnmappedCodeWarning()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-06-15T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-06-15T12:00"],
                [14.5],
                [95],
                [22.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-06-15"],
                [61],
                [16.0],
                [8.0],
                [25.0])));

        var logger = CreateLogger();
        var provider = CreateProvider(new FakeHttpHandler(json), logger);

        await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private class FakeHttpHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
