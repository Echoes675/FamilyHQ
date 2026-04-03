using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

/// <summary>
/// Page object for the Weather settings tab within /settings.
/// Retained for backwards compatibility with WeatherSteps.
/// Delegates navigation to the underlying SettingsPage.
/// </summary>
public class WeatherSettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    private readonly SettingsPage _settingsPage;

    public override string PageUrl => _config.BaseUrl + "/settings";

    public WeatherSettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
        _settingsPage = new SettingsPage(page);
    }

    public ILocator EnabledToggle         => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect  => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput     => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput    => Page.Locator("#wind-threshold");
    public ILocator SaveBtn               => Page.Locator(".settings-btn").First;
    public ILocator CancelBtn             => Page.Locator(".settings-btn--ghost");
    public ILocator SuccessMessage        => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });
    public ILocator BackBtn               => Page.Locator(".dashboard-header__back");

    public async Task NavigateAndWaitAsync()
    {
        await _settingsPage.NavigateToWeatherTabAsync();
    }
}
