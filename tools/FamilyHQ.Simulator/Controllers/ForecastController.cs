namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("v1")]
public class ForecastController(SimContext db) : ControllerBase
{
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        CancellationToken ct)
    {
        var data = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - latitude) < 0.001
                     && Math.Abs(w.Longitude - longitude) < 0.001)
            .ToListAsync(ct);

        var current = data.FirstOrDefault(d => d.DataType == "current");
        var hourly = data.Where(d => d.DataType == "hourly").OrderBy(d => d.Time).ToList();
        var daily = data.Where(d => d.DataType == "daily").OrderBy(d => d.Time).ToList();

        var response = new
        {
            current = current is not null ? new
            {
                time = current.Time,
                temperature_2m = current.Temperature,
                weather_code = current.WeatherCode,
                wind_speed_10m = current.WindSpeed
            } : new
            {
                time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
                temperature_2m = 15.0,
                weather_code = 0,
                wind_speed_10m = 5.0
            },
            hourly = new
            {
                time = hourly.Select(h => h.Time).ToList(),
                temperature_2m = hourly.Select(h => h.Temperature).ToList(),
                weather_code = hourly.Select(h => h.WeatherCode).ToList(),
                wind_speed_10m = hourly.Select(h => h.WindSpeed).ToList()
            },
            daily = new
            {
                time = daily.Select(d => d.Time).ToList(),
                weather_code = daily.Select(d => d.WeatherCode).ToList(),
                temperature_2m_max = daily.Select(d => d.TemperatureMax ?? 15.0).ToList(),
                temperature_2m_min = daily.Select(d => d.TemperatureMin ?? 5.0).ToList(),
                wind_speed_10m_max = daily.Select(d => d.WindSpeedMax ?? 10.0).ToList()
            }
        };

        return Ok(response);
    }
}
