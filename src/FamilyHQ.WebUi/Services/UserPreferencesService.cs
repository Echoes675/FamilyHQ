using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using Blazored.LocalStorage;
using FamilyHQ.WebUi.Models;

namespace FamilyHQ.WebUi.Services;

public sealed class UserPreferencesService : IUserPreferencesService
{
    private const string LocalStorageKey = "familyhq_preferences";
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _httpClient;
    private Timer? _syncDebounceTimer;
    private UserPreferencesDto _current = new();

    public UserPreferencesDto Current => _current;
    public event EventHandler<UserPreferencesDto>? PreferencesChanged;

    public UserPreferencesService(ILocalStorageService localStorage, HttpClient httpClient)
    {
        _localStorage = localStorage;
        _httpClient = httpClient;
    }

    public async Task LoadAsync()
    {
        // 1. Load from LocalStorage first (instant, no flicker)
        var local = await _localStorage.GetItemAsync<UserPreferencesDto>(LocalStorageKey);
        if (local is not null)
        {
            _current = local;
            PreferencesChanged?.Invoke(this, _current);
        }

        // 2. Fetch from backend and update if newer
        try
        {
            var remote = await _httpClient.GetFromJsonAsync<UserPreferencesDto>("/api/preferences");
            if (remote is not null)
            {
                if (local is null || remote.LastModified > local.LastModified)
                {
                    _current = remote;
                    await _localStorage.SetItemAsync(LocalStorageKey, _current);
                    PreferencesChanged?.Invoke(this, _current);
                }
            }
        }
        catch
        {
            // Backend unavailable — use local preferences
        }
    }

    public async Task SaveAsync(UserPreferencesDto preferences)
    {
        _current = preferences;

        // Save to LocalStorage immediately
        await _localStorage.SetItemAsync(LocalStorageKey, _current);
        PreferencesChanged?.Invoke(this, _current);

        // Debounce backend sync (1 second)
        _syncDebounceTimer?.Dispose();
        _syncDebounceTimer = new System.Threading.Timer(async _ => await SyncToBackendAsync(), null, 1000, Timeout.Infinite);
    }

    public Task UpdateEventDensityAsync(int density)
    {
        var updated = new UserPreferencesDto
        {
            EventDensity = Math.Clamp(density, 1, 3),
            CalendarColumnOrder = _current.CalendarColumnOrder,
            CalendarColorOverrides = _current.CalendarColorOverrides
        };
        return SaveAsync(updated);
    }

    public Task UpdateCalendarOrderAsync(List<string> calendarIds)
    {
        var updated = new UserPreferencesDto
        {
            EventDensity = _current.EventDensity,
            CalendarColumnOrder = string.Join(",", calendarIds),
            CalendarColorOverrides = _current.CalendarColorOverrides
        };
        return SaveAsync(updated);
    }

    public Task UpdateCalendarColorAsync(string calendarId, string hexColor)
    {
        var colors = string.IsNullOrEmpty(_current.CalendarColorOverrides)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(_current.CalendarColorOverrides) ?? new();
        colors[calendarId] = hexColor;

        var updated = new UserPreferencesDto
        {
            EventDensity = _current.EventDensity,
            CalendarColumnOrder = _current.CalendarColumnOrder,
            CalendarColorOverrides = JsonSerializer.Serialize(colors)
        };
        return SaveAsync(updated);
    }

    private async Task SyncToBackendAsync()
    {
        try
        {
            await _httpClient.PutAsJsonAsync("/api/preferences", new
        {
            eventDensity = _current.EventDensity,
            calendarColumnOrder = _current.CalendarColumnOrder,
            calendarColorOverrides = _current.CalendarColorOverrides
        });
        }
        catch
        {
            // Silently fail — will sync next time
        }
    }

    public ValueTask DisposeAsync()
    {
        _syncDebounceTimer?.Dispose();
        return ValueTask.CompletedTask;
    }
}