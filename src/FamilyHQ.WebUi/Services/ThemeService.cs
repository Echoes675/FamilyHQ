using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class ThemeService : IThemeService, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly SignalRService _signalRService;
    private readonly Action<string> _themeChangedHandler;
    private IJSObjectReference? _module;

    public ThemeService(HttpClient httpClient, IJSRuntime jsRuntime, SignalRService signalRService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;

        _themeChangedHandler = period => _ = SetThemeAsync(period);
        _signalRService.OnThemeChanged += _themeChangedHandler;
    }

    public async Task InitialiseAsync()
    {
        var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
        if (dto is not null)
            await SetThemeAsync(dto.CurrentPeriod);
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
        {
            await _module.DisposeAsync();
        }
    }
}
