using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public class WeatherSettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/settings/weather";

    public WeatherSettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    // Locators
    public ILocator EnabledToggle => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput => Page.Locator("#wind-threshold");
    public ILocator SaveBtn => Page.Locator(".settings-btn").First;
    public ILocator CancelBtn => Page.Locator(".settings-btn--ghost");
    public ILocator SuccessMessage => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });
    public ILocator BackBtn => Page.Locator(".dashboard-header__back");

    public async Task NavigateAndWaitAsync()
    {
        await NavigateAsync();
        await Page.Locator(".settings-page").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
    }
}
