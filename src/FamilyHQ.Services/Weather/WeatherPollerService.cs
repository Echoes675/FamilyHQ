namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class WeatherPollerService(
    IServiceProvider serviceProvider,
    ILogger<WeatherPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

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
        var refreshService = scope.ServiceProvider.GetRequiredService<IWeatherRefreshService>();
        var weatherSettingRepo = scope.ServiceProvider.GetRequiredService<IWeatherSettingRepository>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<WeatherOptions>>().Value;

        var allSettings = await weatherSettingRepo.GetAllAsync(stoppingToken);

        foreach (var setting in allSettings)
        {
            await refreshService.RefreshAsync(setting.UserId, stoppingToken);
        }

        var minInterval = allSettings.Count > 0
            ? allSettings.Min(s => s.PollIntervalMinutes)
            : options.MinPollIntervalMinutes;

        var pollInterval = TimeSpan.FromMinutes(
            Math.Max(options.MinPollIntervalMinutes, minInterval));

        await Task.Delay(pollInterval, stoppingToken);
    }
}
