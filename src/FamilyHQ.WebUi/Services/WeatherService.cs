using Microsoft.AspNetCore.SignalR.Client;

namespace FamilyHQ.WebUi.Services;

public interface IWeatherService
{
    WeatherConditionDto CurrentCondition { get; }
    event EventHandler<WeatherConditionDto>? WeatherChanged;
    Task StartAsync();
}

public enum WeatherConditionDto
{
    Clear, Cloudy, LightRain, HeavyRain, Thunder, Snow, WindMist
}

public sealed class WeatherService : IWeatherService, IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    public WeatherConditionDto CurrentCondition { get; private set; } = WeatherConditionDto.Clear;
    public event EventHandler<WeatherConditionDto>? WeatherChanged;

    public WeatherService(IConfiguration configuration)
    {
        var apiBaseUrl = configuration["ApiBaseUrl"] ?? "https://localhost:7001";
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{apiBaseUrl}/hubs/weather")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<object>("WeatherUpdated", payload =>
        {
            // Parse the condition from the payload
            // The payload is { condition: string, temperatureCelsius: double?, observedAt: DateTimeOffset }
            if (payload is System.Text.Json.JsonElement json &&
                json.TryGetProperty("condition", out var conditionProp) &&
                Enum.TryParse<WeatherConditionDto>(conditionProp.GetString(), ignoreCase: true, out var condition))
            {
                CurrentCondition = condition;
                WeatherChanged?.Invoke(this, condition);
            }
        });
    }

    public async Task StartAsync()
    {
        try
        {
            await _hubConnection.StartAsync();
        }
        catch
        {
            // Silently fail — weather is non-critical
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _hubConnection.DisposeAsync();
    }
}
