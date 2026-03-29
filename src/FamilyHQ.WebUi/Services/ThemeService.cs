using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class ThemeService : IThemeService
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly SignalRService _signalRService;

    public ThemeService(HttpClient httpClient, IJSRuntime jsRuntime, SignalRService signalRService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;

        _signalRService.OnThemeChanged += async period => await SetThemeAsync(period);
    }

    public async Task InitialiseAsync()
    {
        var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
        if (dto is not null)
            await SetThemeAsync(dto.CurrentPeriod);
    }

    private async Task SetThemeAsync(string period)
    {
        var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await module.InvokeVoidAsync("setTheme", period);
    }
}
