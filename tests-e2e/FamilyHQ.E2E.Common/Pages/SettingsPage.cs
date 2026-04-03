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

    public ILocator BackBtn => Page.Locator(".dashboard-header__back");
    public ILocator LocationHint => Page.Locator(".settings-hint").Filter(new() { HasText = "No location saved" });
    public ILocator LocationPill => Page.Locator(".settings-location-pill");
    public ILocator LocationPillBadge => Page.Locator(".settings-location-pill__badge");
    public ILocator PlaceNameInput => Page.Locator("#place-input");
    public ILocator SaveLocationBtn => Page.GetByTestId("save-location-btn");
    public ILocator MorningTile => Page.Locator(".theme-tile--morning");
    public ILocator DaytimeTile => Page.Locator(".theme-tile--daytime");
    public ILocator EveningTile => Page.Locator(".theme-tile--evening");
    public ILocator NightTile => Page.Locator(".theme-tile--night");
    public ILocator AccountName => Page.Locator(".account-name");
    public ILocator SignOutBtn => Page.Locator(".settings-account")
        .GetByRole(AriaRole.Button, new() { Name = "Sign Out" });
    public ILocator WeatherSettingsLink => Page.Locator(".settings-section").Filter(new() { HasText = "Weather settings" });

    public async Task NavigateAndWaitAsync()
    {
        await NavigateAsync();
        await Page.Locator(".settings-page").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }
}
