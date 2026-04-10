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
    public ILocator GeneralTab  => Page.GetByTestId("tab-general");
    public ILocator LocationTab => Page.GetByTestId("tab-location");
    public ILocator WeatherTab  => Page.GetByTestId("tab-weather");
    public ILocator DisplayTab  => Page.GetByTestId("tab-display");

    // General tab
    public ILocator AccountName => Page.GetByTestId("account-name");
    public ILocator SignOutBtn  => Page.GetByTestId("sign-out-btn");

    // Location tab
    public ILocator LocationHint     => Page.Locator(".settings-hint").Filter(new() { HasText = "No location saved" });
    public ILocator LocationPill     => Page.Locator(".settings-location-pill");
    public ILocator LocationPillBadge => Page.Locator(".settings-location-pill__badge");
    public ILocator PlaceNameInput   => Page.Locator("#place-input");
    public ILocator SaveLocationBtn  => Page.GetByTestId("save-location-btn");

    // Weather tab (for WeatherSteps access)
    public ILocator WeatherEnabledToggle  => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput     => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput    => Page.Locator("#wind-threshold");
    public ILocator WeatherSaveBtn        => Page.Locator(".settings-btn").First;
    public ILocator WeatherCancelBtn      => Page.Locator(".settings-btn--ghost");
    public ILocator WeatherSuccessMessage => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });

    // Calendars tab
    public ILocator CalendarsTab           => Page.GetByTestId("tab-calendars");
    public ILocator CalendarSettingsList   => Page.Locator(".calendar-settings-row");
    public ILocator SharedChangeConfirmBtn => Page.GetByTestId("shared-change-confirm");
    public ILocator SharedChangeCancelBtn  => Page.GetByTestId("shared-change-cancel");

    // Display tab — auto-change toggle
    public ILocator AutoThemeToggle => Page.Locator("#auto-theme-toggle");

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

    public async Task NavigateToWeatherTabAsync()
    {
        await NavigateAndWaitAsync();
        await WeatherTab.ClickAsync();
        await WeatherEnabledToggle.WaitForAsync(
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

    public async Task ClickSyncNowAsync()
    {
        // The sync call hits POST /api/sync/trigger.  We wait for the response
        // before checking the success banner so the test doesn't race against
        // Blazor's state update.
        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/sync/trigger") && r.Status == 200,
            new() { Timeout = 30000 });

        await SyncNowBtn.ClickAsync();
        await responseTask;
    }
}
