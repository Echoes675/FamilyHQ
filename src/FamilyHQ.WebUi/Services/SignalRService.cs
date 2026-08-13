using Microsoft.AspNetCore.SignalR.Client;

namespace FamilyHQ.WebUi.Services;

public class SignalRService : IAsyncDisposable, ISignalRConnectionEvents
{
    private readonly HubConnection _hubConnection;
    private readonly ISignalRConnectionCoordinator _coordinator;
    private bool _disposing;

    public event Action? OnEventsUpdated;
    public event Action? OnConnectionStatusUpdated;
    public event Action<string>? OnThemeChanged;
    public event Action? OnWeatherUpdated;
    public event Action? Reconnected;

    public SignalRService(string backendUrl, ISignalRConnectionCoordinator coordinator)
    {
        _coordinator = coordinator;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{backendUrl.TrimEnd('/')}/hubs/calendar")
            .WithAutomaticReconnect()
            .Build();

        _coordinator.Initialize(ct => _hubConnection.StartAsync(ct));
        _coordinator.ConnectionRestored += () => Reconnected?.Invoke();

        _hubConnection.On("EventsUpdated", () =>
        {
            OnEventsUpdated?.Invoke();
        });

        _hubConnection.On("ConnectionStatusUpdated", () =>
        {
            OnConnectionStatusUpdated?.Invoke();
        });

        _hubConnection.On<string>("ThemeChanged", period => OnThemeChanged?.Invoke(period));

        _hubConnection.On("WeatherUpdated", () => OnWeatherUpdated?.Invoke());

        _hubConnection.Reconnecting += exception =>
        {
            _coordinator.OnReconnecting(exception);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _coordinator.OnReconnected();
            return Task.CompletedTask;
        };

        _hubConnection.Closed += exception =>
        {
            // Closed also fires on deliberate disposal — that is not a failure.
            if (!_disposing)
            {
                _coordinator.OnClosed(exception);
            }

            return Task.CompletedTask;
        };
    }

    public async Task StartAsync()
    {
        // Re-entered every time the dashboard page initialises; only a
        // disconnected hub may be started (starting twice throws).
        if (_hubConnection.State != HubConnectionState.Disconnected)
        {
            return;
        }

        try
        {
            await _hubConnection.StartAsync();
            _coordinator.OnStarted();
        }
        catch (Exception ex)
        {
            // WithAutomaticReconnect() does NOT retry a failed initial start —
            // the coordinator logs the failure and schedules background restarts.
            _coordinator.OnStartFailed(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
