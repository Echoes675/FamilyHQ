using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public class SettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/settings";

    public SettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    // Header
    public ILocator BackBtn => Page.Locator(".dashboard-header__back");

    // Tab navigation
    public ILocator GeneralTab          => Page.GetByTestId("tab-general");
    public ILocator LocationTab         => Page.GetByTestId("tab-location");
    public ILocator WeatherTab          => Page.GetByTestId("tab-weather");
    public ILocator DisplayTab          => Page.GetByTestId("tab-display");
    public ILocator WeatherOverrideTab  => Page.GetByTestId("tab-weather-override");
    public ILocator DiagnosticsTab      => Page.GetByTestId("tab-diagnostics");

    // General tab
    public ILocator AccountName => Page.GetByTestId("account-name");
    public ILocator SignOutBtn  => Page.GetByTestId("sign-out-btn");

    // Location tab
    public ILocator LocationHint     => Page.Locator(".settings-hint").Filter(new() { HasText = "No location saved" });
    public ILocator LocationPill      => Page.GetByTestId("location-pill");
    public ILocator LocationPillBadge => Page.GetByTestId("location-badge");
    public ILocator PlaceNameInput   => Page.Locator("#place-input");
    public ILocator SaveLocationBtn  => Page.GetByTestId("save-location-btn");
    // The "Reset to auto-detect" ghost button under the location section (rendered only when a
    // location is explicitly saved). The time-zone section's reset button has its own testid.
    public ILocator ResetLocationBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Reset to auto-detect" }).First;

    // Location tab — time zone section (FHQ-43)
    public ILocator TimeZoneSelect      => Page.GetByTestId("tz-select");
    public ILocator SaveTimeZoneBtn     => Page.GetByTestId("save-timezone-btn");
    public ILocator ResetTimeZoneBtn    => Page.GetByTestId("reset-timezone-btn");
    public ILocator TimeZonePill        => Page.GetByTestId("tz-pill");
    public ILocator TimeZoneBadge       => Page.GetByTestId("tz-badge");
    public ILocator TimeZoneEffective   => Page.GetByTestId("tz-effective-zone");

    // Weather tab (for WeatherSteps access)
    public ILocator WeatherEnabledToggle  => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput     => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput    => Page.Locator("#wind-threshold");
    public ILocator WeatherSaveBtn        => Page.Locator(".settings-btn").First;
    public ILocator WeatherCancelBtn      => Page.Locator(".settings-btn--ghost");
    public ILocator WeatherSuccessMessage => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });

    // Weather Override tab (dev/staging only)
    public ILocator WeatherOverrideToggle    => Page.GetByTestId("weather-override-toggle");
    public ILocator WeatherOverrideWindyPill => Page.GetByTestId("weather-override-windy");
    public ILocator WeatherOverrideConditionPill(string condition) =>
        Page.GetByTestId($"weather-override-condition-{condition}");

    // Calendars tab
    public ILocator CalendarsTab           => Page.GetByTestId("tab-calendars");
    public ILocator CalendarSettingsList   => Page.Locator(".calendar-settings-row");
    public ILocator SharedChangeConfirmBtn => Page.GetByTestId("shared-change-confirm");
    public ILocator SharedChangeCancelBtn  => Page.GetByTestId("shared-change-cancel");

    // Display tab — auto-change toggle
    public ILocator AutoThemeToggle => Page.Locator("#auto-theme-toggle");

    public async Task<bool> IsAutoThemeOnAsync()
    {
        var pressed = await AutoThemeToggle.GetAttributeAsync("aria-pressed");
        return string.Equals(pressed, "true", StringComparison.OrdinalIgnoreCase);
    }

    // Display tab — theme tiles
    public ILocator MorningTile => Page.GetByTestId("theme-tile-morning");
    public ILocator DaytimeTile => Page.GetByTestId("theme-tile-daytime");
    public ILocator EveningTile => Page.GetByTestId("theme-tile-evening");
    public ILocator NightTile   => Page.GetByTestId("theme-tile-night");

    public ILocator ThemeTile(string name) => Page.GetByTestId($"theme-tile-{name.ToLower()}");

    public async Task NavigateAndWaitAsync()
    {
        await NavigateAsync();
        await Page.Locator(".settings-page--tabbed").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task NavigateToLocationTabAsync()
    {
        await NavigateAndWaitAsync();
        await LocationTab.ClickAsync();
        await PlaceNameInput.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    // FHQ-43: selects an IANA zone in the time-zone override dropdown and saves it. The Save
    // button is disabled until a zone is selected and re-disables after the save (the component
    // clears the selection), so we drive the select then click. Returns once the request the
    // component fires has completed, so callers don't race the post-save re-render.
    public async Task SelectAndSaveTimeZoneAsync(string ianaZone)
    {
        await TimeZoneSelect.SelectOptionAsync(new SelectOptionValue { Value = ianaZone });

        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/settings/timezone") && r.Request.Method == "PUT",
            new() { Timeout = 30000 });
        await SaveTimeZoneBtn.ClickAsync();
        await responseTask;
    }

    // FHQ-43: resets the explicit time-zone override back to auto-detect via the section's reset
    // button (only rendered while an explicit zone is saved). Waits for the DELETE to complete.
    public async Task ResetTimeZoneToAutoAsync()
    {
        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/settings/timezone") && r.Request.Method == "DELETE",
            new() { Timeout = 30000 });
        await ResetTimeZoneBtn.ClickAsync();
        await responseTask;
    }

    public async Task NavigateToWeatherTabAsync()
    {
        await NavigateAndWaitAsync();
        await WeatherTab.ClickAsync();
        await WeatherEnabledToggle.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    public async Task NavigateToWeatherOverrideTabAsync()
    {
        await NavigateAndWaitAsync();
        await WeatherOverrideTab.ClickAsync();
        await Page.GetByTestId("weather-override-toggle").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    public async Task NavigateToCalendarsTabAsync()
    {
        await NavigateAndWaitAsync();
        await CalendarsTab.ClickAsync();
        await CalendarSettingsList.First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    public ILocator GetCalendarSettingsItem(string calendarName)
        => Page.GetByTestId($"calendar-row-{calendarName}");

    public ILocator GetVisibilityToggle(string calendarName)
        => Page.GetByTestId($"visibility-toggle-{calendarName}");

    public ILocator GetSharedToggle(string calendarName)
        => Page.GetByTestId($"shared-toggle-{calendarName}");

    public async Task HideCalendarAsync(string calendarName)
    {
        var toggle = GetVisibilityToggle(calendarName);
        var classes = await toggle.GetAttributeAsync("class") ?? string.Empty;
        if (classes.Contains("pill-toggle--on"))
        {
            await toggle.ClickAsync();
            // Wait for the async Blazor save to complete before returning
            await Page.Locator(".alert-success").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
    }

    public async Task<bool> IsCalendarDesignatedSharedAsync(string calendarName)
    {
        var toggle = GetSharedToggle(calendarName);
        var classes = await toggle.GetAttributeAsync("class") ?? string.Empty;
        return classes.Contains("pill-toggle--on");
    }

    public async Task DesignateSharedCalendarAsync(string calendarName)
    {
        // Already shared? Button is a no-op.
        if (await IsCalendarDesignatedSharedAsync(calendarName))
            return;

        await GetSharedToggle(calendarName).ClickAsync();
        // Confirmation modal appears — confirm the change.
        await SharedChangeConfirmBtn.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await SharedChangeConfirmBtn.ClickAsync();
        // Wait for the save to complete before returning.
        await Page.Locator(".alert-success").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    public ILocator SyncNowBtn => Page.GetByTestId("sync-now-btn");
    public ILocator RegisterWebhooksBtn => Page.GetByTestId("register-webhooks-btn");

    // General tab — diagnostics link (removed in FHQ-62; kept for absence assertions)
    public ILocator DiagnosticsLink => Page.GetByTestId("settings-diagnostics-link");

    // Diagnostics tab
    public ILocator DiagnosticsConnectionHeading => Page.GetByTestId("diagnostics-connection-heading");

    // Calendars tab — reauth banner (mirrors the dashboard banner)
    public ILocator ReauthBannerSettings    => Page.GetByTestId("reauth-banner-settings");
    public ILocator ReauthBannerSettingsCta => Page.GetByTestId("reauth-banner-settings-cta");

    public async Task<int> ClickSyncNowAsync()
    {
        // The sync call hits POST /api/sync/trigger. We wait for the response
        // before returning so callers don't race Blazor's state update.
        // We do NOT filter on Status == 200 because the resilience scenarios
        // expect a 409 (reauth required) and the click must still complete cleanly.
        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/sync/trigger"),
            new() { Timeout = 30000 });

        await SyncNowBtn.ClickAsync();
        var response = await responseTask;
        return response.Status;
    }

    public async Task ClickRegisterWebhooksAsync()
    {
        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/sync/register-webhooks") && r.Status == 200,
            new() { Timeout = 30000 });

        await RegisterWebhooksBtn.ClickAsync();
        await responseTask;
    }
}
