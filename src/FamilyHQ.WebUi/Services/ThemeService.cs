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
    private readonly Action<string> _themeChangedHandler;
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

        _themeChangedHandler = period => _ = ApplyPushedThemeChangeAsync(period);
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

    private async Task ApplyPushedThemeChangeAsync(string period)
    {
        // Fire-and-forget from the SignalR handler — exceptions must be
        // observed here or they vanish silently (FHQ-125).
        try
        {
            if (_displaySettingService.IsAutoTheme)
                await SetThemeAsync(period);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply pushed theme change for {Period}", period);
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
