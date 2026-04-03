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

    // Display tab — theme tiles
    public ILocator MorningTile => Page.GetByTestId("theme-tile-morning");
    public ILocator DaytimeTile => Page.GetByTestId("theme-tile-daytime");
    public ILocator EveningTile => Page.GetByTestId("theme-tile-evening");
    public ILocator NightTile   => Page.GetByTestId("theme-tile-night");

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
}
