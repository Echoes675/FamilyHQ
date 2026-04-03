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
    private readonly Action<string> _themeChangedHandler;
    private IJSObjectReference? _module;

    public ThemeService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        SignalRService signalRService,
        IDisplaySettingService displaySettingService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;
        _displaySettingService = displaySettingService;

        _themeChangedHandler = period =>
        {
            if (_displaySettingService.IsAutoTheme)
                _ = SetThemeAsync(period);
        };
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
        catch (HttpRequestException)
        {
            // Theme is non-critical — fall back to default if API is unreachable or returns an error
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
        catch (HttpRequestException) { }
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
