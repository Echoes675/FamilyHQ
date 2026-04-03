using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class DisplaySettingService : IDisplaySettingService, IAsyncDisposable
{
    private readonly ISettingsApiService _settingsApi;
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;

    public DisplaySettingService(ISettingsApiService settingsApi, IJSRuntime jsRuntime)
    {
        _settingsApi = settingsApi;
        _jsRuntime = jsRuntime;
    }

    public DisplaySettingDto CurrentSettings { get; private set; } =
        new(1.0, false, 15, "auto");

    public bool IsAutoTheme => CurrentSettings.ThemeSelection == "auto";

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

    public async Task ApplyManualThemeAsync(string themeName)
    {
        var module = await GetModuleAsync();
        // Manual selection is always instant — no transition when auto is OFF.
        await module.InvokeVoidAsync("setDisplayProperty", "--theme-transition-duration", "0s");
        await module.InvokeVoidAsync("setTheme", themeName);
    }

    private async Task ApplyAllPropertiesAsync()
    {
        var module = await GetModuleAsync();

        var multiplier = CurrentSettings.OpaqueSurfaces ? "1.0" : CurrentSettings.SurfaceMultiplier.ToString("F2");
        await module.InvokeVoidAsync("setDisplayProperty", "--user-surface-multiplier", multiplier);

        // Transition speed applies only when auto-change is ON.
        // Manual theme selection is always instant.
        var duration = IsAutoTheme ? $"{CurrentSettings.TransitionDurationSecs}s" : "0s";
        await module.InvokeVoidAsync("setDisplayProperty", "--theme-transition-duration", duration);

        // Apply manual theme if set
        if (!IsAutoTheme)
            await module.InvokeVoidAsync("setTheme", CurrentSettings.ThemeSelection);
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
