using FamilyHQ.Core.Models;
using FamilyHQ.Services.Weather;
using FamilyHQ.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace FamilyHQ.WebApi.Services;

public sealed class WeatherBroadcastService : IHostedService, IDisposable
{
    private readonly WeatherBackgroundService _weatherService;
    private readonly IHubContext<WeatherHub> _hubContext;

    public WeatherBroadcastService(
        WeatherBackgroundService weatherService,
        IHubContext<WeatherHub> hubContext)
    {
        _weatherService = weatherService;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _weatherService.WeatherUpdated += OnWeatherUpdated;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _weatherService.WeatherUpdated -= OnWeatherUpdated;
        return Task.CompletedTask;
    }

    private void OnWeatherUpdated(object? sender, WeatherState state)
    {
        _ = _hubContext.Clients.All.SendAsync("WeatherUpdated", new
        {
            condition = state.Condition.ToString(),
            temperatureCelsius = state.TemperatureCelsius,
            observedAt = state.ObservedAt
        });
    }

    public void Dispose() { }
}
