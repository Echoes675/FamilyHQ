using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class ThemeService : IThemeService, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly SignalRService _signalRService;
    private readonly IDisplaySettingService _displaySettingService;
    private readonly ILogger<ThemeService> _logger;
    private readonly Action _themeChangedHandler;
    private IJSObjectReference? _module;

    public ThemeService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        SignalRService signalRService,
        IDisplaySettingService displaySettingService,
        ILogger<ThemeService> logger)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;
        _displaySettingService = displaySettingService;
        _logger = logger;

        // FHQ-177: the push is a bare signal. Re-read our own period rather than trusting a
        // broadcast value, which would be another kiosk's answer whenever they differ.
        _themeChangedHandler = () => _ = ApplyPushedThemeChangeAsync();
        _signalRService.OnThemeChanged += _themeChangedHandler;
    }

    public async Task InitialiseAsync()
    {
        // DisplaySettingService.InitialiseAsync() runs before ThemeService.InitialiseAsync()
        // so CurrentSettings.ThemeSelection is already loaded.
        if (!_displaySettingService.IsAutoTheme)
        {
            await SetThemeAsync(_displaySettingService.CurrentSettings.ThemeSelection);
            return;
        }

        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
            if (dto is not null)
                await SetThemeAsync(dto.CurrentPeriod);
        }
        catch (HttpRequestException ex)
        {
            // Theme is non-critical — fall back to default if API is unreachable or returns an error
            _logger.LogDebug(ex, "Theme API unreachable during initialise; using default theme.");
        }
    }

    public async Task ApplyCurrentPeriodAsync()
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
            if (dto is not null)
                await SetThemeAsync(dto.CurrentPeriod);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Failed to apply current theme period; leaving theme unchanged.");
        }
    }

    private async Task ApplyPushedThemeChangeAsync()
    {
        // Fire-and-forget from the SignalR handler — exceptions must be
        // observed here or they vanish silently (FHQ-125).
        try
        {
            // FHQ-177: the signal no longer carries a period, so re-read ours. The IsAutoTheme guard
            // still matters and does NOT live in ApplyCurrentPeriodAsync: that method is also called
            // from Settings at the moment auto-theme is switched on, when the flag has not been
            // written yet. Dropping the guard here would let a boundary override a manually chosen
            // theme.
            if (_displaySettingService.IsAutoTheme)
                await ApplyCurrentPeriodAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply pushed theme change");
        }
    }

    private async Task SetThemeAsync(string period)
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await _module.InvokeVoidAsync("setTheme", period);
    }

    public async ValueTask DisposeAsync()
    {
        _signalRService.OnThemeChanged -= _themeChangedHandler;
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
