namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/simulator/backdoor/weather")]
public class BackdoorWeatherController(SimContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SetWeather([FromBody] SetWeatherRequest request, CancellationToken ct)
    {
        // Remove existing data for this location (using approximate coordinate matching)
        var existing = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - request.Latitude) < 0.001
                     && Math.Abs(w.Longitude - request.Longitude) < 0.001)
            .ToListAsync(ct);
        db.SimulatedWeather.RemoveRange(existing);

        // Add current
        if (request.Current is not null)
        {
            db.SimulatedWeather.Add(new SimulatedWeather
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                DataType = "current",
                Time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
                WeatherCode = request.Current.WeatherCode,
                Temperature = request.Current.Temperature,
                WindSpeed = request.Current.WindSpeed
            });
        }

        // Add hourly
        if (request.Hourly is not null)
        {
            foreach (var h in request.Hourly)
            {
                db.SimulatedWeather.Add(new SimulatedWeather
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    DataType = "hourly",
                    Time = h.Time,
                    WeatherCode = h.WeatherCode,
                    Temperature = h.Temperature,
                    WindSpeed = h.WindSpeed
                });
            }
        }

        // Add daily
        if (request.Daily is not null)
        {
            foreach (var d in request.Daily)
            {
                db.SimulatedWeather.Add(new SimulatedWeather
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    DataType = "daily",
                    Time = d.Date,
                    WeatherCode = d.WeatherCode,
                    TemperatureMax = d.TemperatureMax,
                    TemperatureMin = d.TemperatureMin,
                    WindSpeedMax = d.WindSpeedMax
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Weather data set" });
    }

    [HttpDelete]
    public async Task<IActionResult> ClearWeather(
        [FromQuery] double latitude, [FromQuery] double longitude, CancellationToken ct)
    {
        var existing = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - latitude) < 0.001
                     && Math.Abs(w.Longitude - longitude) < 0.001)
            .ToListAsync(ct);
        db.SimulatedWeather.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Weather data cleared" });
    }
}

public record SetWeatherRequest(
    double Latitude,
    double Longitude,
    SetWeatherCurrentRequest? Current,
    List<SetWeatherHourlyRequest>? Hourly,
    List<SetWeatherDailyRequest>? Daily);

public record SetWeatherCurrentRequest(int WeatherCode, double Temperature, double WindSpeed);
public record SetWeatherHourlyRequest(string Time, int WeatherCode, double Temperature, double WindSpeed);
public record SetWeatherDailyRequest(string Date, int WeatherCode, double TemperatureMax, double TemperatureMin, double WindSpeedMax);
