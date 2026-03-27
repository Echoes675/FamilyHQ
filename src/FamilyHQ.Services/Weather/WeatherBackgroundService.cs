using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Weather;

/// <summary>
/// Background service that polls the weather provider every 15 minutes
/// and stores the latest weather state in memory for SignalR broadcasting.
/// </summary>
public sealed class WeatherBackgroundService : BackgroundService
{
    private readonly IWeatherProvider _weatherProvider;
    private readonly ILogger<WeatherBackgroundService> _logger;
    private WeatherState? _currentState;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    public WeatherBackgroundService(
        IWeatherProvider weatherProvider,
        ILogger<WeatherBackgroundService> logger)
    {
        _weatherProvider = weatherProvider;
        _logger = logger;
    }

    public WeatherState? CurrentState => _currentState;

    public event EventHandler<WeatherState>? WeatherUpdated;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WeatherBackgroundService starting");
        
        // Poll immediately on startup
        await PollWeatherAsync(stoppingToken);
        
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollWeatherAsync(stoppingToken);
        }
    }

    private async Task PollWeatherAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await _weatherProvider.GetCurrentWeatherAsync(cancellationToken);
            if (state is not null)
            {
                _currentState = state;
                WeatherUpdated?.Invoke(this, state);
                _logger.LogDebug("Weather updated: {Condition}", state.Condition);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to poll weather provider");
        }
    }
}
