namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class WeatherPollerService(
    IServiceProvider serviceProvider,
    IWeatherBroadcaster weatherBroadcaster,
    ILogger<WeatherPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DisabledPollDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NoLocationDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPollIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weather poll iteration failed. Retrying in {Delay}.", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task RunPollIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<WeatherOptions>>().Value;
        var weatherSettingRepo = scope.ServiceProvider.GetRequiredService<IWeatherSettingRepository>();
        var weatherSetting = await weatherSettingRepo.GetOrCreateAsync(stoppingToken);

        if (!weatherSetting.Enabled)
        {
            logger.LogInformation("Weather polling is disabled. Checking again in {Delay}.", DisabledPollDelay);
            await Task.Delay(DisabledPollDelay, stoppingToken);
            return;
        }

        var locationRepo = scope.ServiceProvider.GetRequiredService<ILocationSettingRepository>();
        var location = await locationRepo.GetAsync(stoppingToken);

        if (location is null)
        {
            logger.LogInformation("No location configured. Skipping weather poll. Retrying in {Delay}.", NoLocationDelay);
            await Task.Delay(NoLocationDelay, stoppingToken);
            return;
        }

        var weatherProvider = scope.ServiceProvider.GetRequiredService<IWeatherProvider>();
        var weatherResponse = await weatherProvider.GetWeatherAsync(location.Latitude, location.Longitude, stoppingToken);

        var now = DateTimeOffset.UtcNow;
        var windThreshold = weatherSetting.WindThresholdKmh;

        var dataPoints = BuildDataPoints(location.Id, weatherResponse, now, windThreshold);

        var weatherDataPointRepo = scope.ServiceProvider.GetRequiredService<IWeatherDataPointRepository>();
        await weatherDataPointRepo.ReplaceAllAsync(location.Id, dataPoints, stoppingToken);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(stoppingToken);

        logger.LogInformation("Weather data updated for location {PlaceName} ({Lat}, {Lon})",
            location.PlaceName, location.Latitude, location.Longitude);

        var pollInterval = TimeSpan.FromMinutes(
            Math.Max(options.MinPollIntervalMinutes, weatherSetting.PollIntervalMinutes));

        await Task.Delay(pollInterval, stoppingToken);
    }

    private static List<WeatherDataPoint> BuildDataPoints(
        int locationSettingId,
        Core.DTOs.WeatherResponse response,
        DateTimeOffset retrievedAt,
        double windThresholdKmh)
    {
        var dataPoints = new List<WeatherDataPoint>();

        // Current
        dataPoints.Add(new WeatherDataPoint
        {
            LocationSettingId = locationSettingId,
            Timestamp = retrievedAt,
            Condition = response.CurrentCondition,
            TemperatureCelsius = response.CurrentTemperatureCelsius,
            WindSpeedKmh = response.CurrentWindSpeedKmh,
            IsWindy = response.CurrentWindSpeedKmh >= windThresholdKmh,
            DataType = WeatherDataType.Current,
            RetrievedAt = retrievedAt
        });

        // Hourly
        foreach (var hourly in response.HourlyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = hourly.Time,
                Condition = hourly.Condition,
                TemperatureCelsius = hourly.TemperatureCelsius,
                WindSpeedKmh = hourly.WindSpeedKmh,
                IsWindy = hourly.WindSpeedKmh >= windThresholdKmh,
                DataType = WeatherDataType.Hourly,
                RetrievedAt = retrievedAt
            });
        }

        // Daily
        foreach (var daily in response.DailyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = new DateTimeOffset(daily.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Condition = daily.Condition,
                TemperatureCelsius = daily.HighCelsius,
                HighCelsius = daily.HighCelsius,
                LowCelsius = daily.LowCelsius,
                WindSpeedKmh = daily.WindSpeedMaxKmh,
                IsWindy = daily.WindSpeedMaxKmh >= windThresholdKmh,
                DataType = WeatherDataType.Daily,
                RetrievedAt = retrievedAt
            });
        }

        return dataPoints;
    }
}
