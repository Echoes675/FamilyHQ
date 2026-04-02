namespace FamilyHQ.Services.Weather;

using System.Globalization;
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;

public class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    public async Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);

        var url = $"/v1/forecast?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,weather_code,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,wind_speed_10m_max"
            + "&forecast_days=16"
            + "&timezone=auto";

        var apiResponse = await httpClient.GetFromJsonAsync<OpenMeteoApiResponse>(url, ct)
            ?? throw new InvalidOperationException("Weather API returned null response.");

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
                hourly.Add(new WeatherHourlyItem(
                    DateTimeOffset.Parse(apiResponse.Hourly.Time[i], CultureInfo.InvariantCulture),
                    WmoCodeMapper.ToCondition(apiResponse.Hourly.WeatherCode[i]),
                    apiResponse.Hourly.Temperature[i],
                    apiResponse.Hourly.WindSpeed[i]));
            }
        }

        var daily = new List<WeatherDailyItem>();
        if (apiResponse.Daily is not null)
        {
            for (var i = 0; i < apiResponse.Daily.Time.Count; i++)
            {
                daily.Add(new WeatherDailyItem(
                    DateOnly.Parse(apiResponse.Daily.Time[i], CultureInfo.InvariantCulture),
                    WmoCodeMapper.ToCondition(apiResponse.Daily.WeatherCode[i]),
                    apiResponse.Daily.TemperatureMax[i],
                    apiResponse.Daily.TemperatureMin[i],
                    apiResponse.Daily.WindSpeedMax[i]));
            }
        }

        return new WeatherResponse(currentCondition, currentTemp, currentWind, hourly, daily);
    }
}
