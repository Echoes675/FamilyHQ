namespace FamilyHQ.Services.Weather;

using System.Globalization;
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using NodaTime;
using NodaTime.Text;

public class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    // Open-Meteo returns minute-precision timestamps: "2026-06-18T14:00"
    private static readonly LocalDateTimePattern OpenMeteoLocalDateTimePattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm");

    public async Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude,
        string? ianaTimeZone, CancellationToken ct = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);

        var url = $"v1/forecast?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,weather_code,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,wind_speed_10m_max"
            + "&forecast_days=16"
            + "&timezone=auto";

        var apiResponse = await httpClient.GetFromJsonAsync<OpenMeteoApiResponse>(url, ct)
            ?? throw new InvalidOperationException("Weather API returned null response.");

        var zone = ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null;

        var currentCondition = WeatherCondition.Clear;
        var currentTemp = 0.0;
        var currentWind = 0.0;

        if (apiResponse.Current is not null)
        {
            currentCondition = WmoCodeMapper.ToCondition(apiResponse.Current.WeatherCode);
            currentTemp = apiResponse.Current.Temperature;
            currentWind = apiResponse.Current.WindSpeed;
        }

        var hourly = new List<WeatherHourlyItem>();
        if (apiResponse.Hourly is not null)
        {
            for (var i = 0; i < apiResponse.Hourly.Time.Count; i++)
            {
                var temp = apiResponse.Hourly.Temperature[i];
                var code = apiResponse.Hourly.WeatherCode[i];
                var wind = apiResponse.Hourly.WindSpeed[i];
                if (temp is null || code is null || wind is null) continue;
                hourly.Add(new WeatherHourlyItem(
                    ToLocalDateTimeOffset(apiResponse.Hourly.Time[i], zone),
                    WmoCodeMapper.ToCondition(code.Value),
                    temp.Value,
                    wind.Value));
            }
        }

        var daily = new List<WeatherDailyItem>();
        if (apiResponse.Daily is not null)
        {
            for (var i = 0; i < apiResponse.Daily.Time.Count; i++)
            {
                var code = apiResponse.Daily.WeatherCode[i];
                var max = apiResponse.Daily.TemperatureMax[i];
                var min = apiResponse.Daily.TemperatureMin[i];
                var wind = apiResponse.Daily.WindSpeedMax[i];
                if (code is null || max is null || min is null || wind is null) continue;
                daily.Add(new WeatherDailyItem(
                    DateOnly.Parse(apiResponse.Daily.Time[i], CultureInfo.InvariantCulture),
                    WmoCodeMapper.ToCondition(code.Value),
                    max.Value,
                    min.Value,
                    wind.Value));
            }
        }

        return new WeatherResponse(currentCondition, currentTemp, currentWind, hourly, daily);
    }

    private static DateTimeOffset ToLocalDateTimeOffset(string s, DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var local = OpenMeteoLocalDateTimePattern.Parse(s).Value;
            // AtLeniently: spring-forward gaps and fall-back ambiguity handled gracefully
            // for a weather display context.
            return zone.AtLeniently(local).ToDateTimeOffset();
        }
        return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
}
