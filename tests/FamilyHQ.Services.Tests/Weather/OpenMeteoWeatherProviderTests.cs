namespace FamilyHQ.Services.Tests.Weather;

using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class OpenMeteoWeatherProviderTests
{
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
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var provider = new OpenMeteoWeatherProvider(httpClient);

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
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var provider = new OpenMeteoWeatherProvider(httpClient);

        var result = await provider.GetWeatherAsync(53.35, -6.26, "Europe/Dublin");

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.FromHours(1),
            "Europe/Dublin in BST is UTC+1; the offset-less '14:00' should be treated as 14:00+01:00");
        hourlyTimestamp.UtcDateTime.Hour.Should().Be(13,
            "14:00 BST = 13:00 UTC");
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
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var provider = new OpenMeteoWeatherProvider(httpClient);

        var result = await provider.GetWeatherAsync(53.35, -6.26, ianaTimeZone: null);

        var hourlyTimestamp = result.HourlyForecasts[0].Time;
        hourlyTimestamp.Offset.Should().Be(TimeSpan.Zero,
            "null zone falls back to AssumeUniversal — offset-less '14:00' treated as UTC");
        hourlyTimestamp.UtcDateTime.Hour.Should().Be(14);
    }

    private class FakeHttpHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
