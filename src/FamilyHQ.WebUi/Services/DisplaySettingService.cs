using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class DisplaySettingService : IDisplaySettingService, IAsyncDisposable
{
    private readonly ISettingsApiService _settingsApi;
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;

    public DisplaySettingDto CurrentSettings { get; private set; } =
        new(1.0, false, 15);

    public DisplaySettingService(ISettingsApiService settingsApi, IJSRuntime jsRuntime)
    {
        _settingsApi = settingsApi;
        _jsRuntime = jsRuntime;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            CurrentSettings = await _settingsApi.GetDisplayAsync();
        }
        catch
        {
            // Use defaults if API call fails
        }

        await ApplyAllPropertiesAsync();
    }

    public async Task UpdatePropertyAsync(string cssPropertyName, string value)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("setDisplayProperty", cssPropertyName, value);
    }

    public async Task SaveAsync(DisplaySettingDto dto)
    {
        CurrentSettings = await _settingsApi.SaveDisplayAsync(dto);
        await ApplyAllPropertiesAsync();
    }

    private async Task ApplyAllPropertiesAsync()
    {
        var module = await GetModuleAsync();

        var multiplier = CurrentSettings.OpaqueSurfaces ? "100" : CurrentSettings.SurfaceMultiplier.ToString("F2");
        await module.InvokeVoidAsync("setDisplayProperty", "--user-surface-multiplier", multiplier);

        var duration = $"{CurrentSettings.TransitionDurationSecs}s";
        await module.InvokeVoidAsync("setDisplayProperty", "--theme-transition-duration", duration);
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        return _module;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
