using Microsoft.AspNetCore.SignalR.Client;

namespace FamilyHQ.WebUi.Services;

public class SignalRService : IAsyncDisposable, ISignalRConnectionEvents
{
    private readonly HubConnection _hubConnection;
    public event Action? OnEventsUpdated;
    public event Action<string>? OnThemeChanged;
    public event Action? OnWeatherUpdated;
    public event Action? Reconnected;

    public SignalRService(string backendUrl)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{backendUrl.TrimEnd('/')}/hubs/calendar")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On("EventsUpdated", () =>
        {
            OnEventsUpdated?.Invoke();
        });

        _hubConnection.On<string>("ThemeChanged", period => OnThemeChanged?.Invoke(period));

        _hubConnection.On("WeatherUpdated", () => OnWeatherUpdated?.Invoke());

        _hubConnection.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };
    }

    public async Task StartAsync()
    {
        try
        {
            await _hubConnection.StartAsync();
        }
        catch
        {
            // Background reconnect handles failures
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
