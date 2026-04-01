using Microsoft.AspNetCore.SignalR.Client;

namespace FamilyHQ.WebUi.Services;

public class SignalRService : IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    public event Action? OnEventsUpdated;
    public event Action<string>? OnThemeChanged;

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
