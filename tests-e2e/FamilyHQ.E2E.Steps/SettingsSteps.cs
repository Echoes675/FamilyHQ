using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class SettingsSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SettingsPage _settingsPage;
    private readonly DashboardPage _dashboardPage;

    public SettingsSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(page);
        _dashboardPage = new DashboardPage(page);
    }

    [Given(@"I am on the settings page")]
    public async Task GivenIAmOnTheSettingsPage()
    {
        await _settingsPage.NavigateAndWaitAsync();
    }

    [Then(@"I am on the settings page")]
    public async Task ThenIAmOnTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        page.Url.Should().EndWith("/settings");
        await Assertions.Expect(_settingsPage.AccountName).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    [When(@"I navigate to the settings page")]
    public async Task WhenINavigateToTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"I navigate to the location tab")]
    public async Task WhenINavigateToTheLocationTab()
    {
        await _settingsPage.LocationTab.ClickAsync();
        await _settingsPage.PlaceNameInput.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I navigate to the weather tab")]
    public async Task WhenINavigateToTheWeatherTab()
    {
        await _settingsPage.WeatherTab.ClickAsync();
        await _settingsPage.WeatherEnabledToggle.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I navigate to the display tab")]
    public async Task WhenINavigateToTheDisplayTab()
    {
        await _settingsPage.DisplayTab.ClickAsync();
        await _settingsPage.MorningTile.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I click the back button")]
    public async Task WhenIClickTheBackButton()
    {
        await _settingsPage.BackBtn.ClickAsync();
    }

    [When(@"I enter ""([^""]*)"" as the place name")]
    public async Task WhenIEnterAsThePlaceName(string placeName)
    {
        await _settingsPage.PlaceNameInput.FillAsync(placeName);
    }

    [When(@"I click save location")]
    public async Task WhenIClickSaveLocation()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SaveLocationBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
    }

    [When(@"I click the sign out button on the settings page")]
    public async Task WhenIClickTheSignOutButtonOnTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SignOutBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"I see the no saved location hint")]
    public async Task ThenISeeTheNoSavedLocationHint()
    {
        await _settingsPage.LocationHint.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    [Then(@"I see the location pill displaying ""([^""]*)""")]
    public async Task ThenISeeTheLocationPillDisplaying(string placeName)
    {
        // The pill is visible both before and after the save (pre-save it shows the
        // auto-detected city).  A one-shot InnerTextAsync() read races the post-save
        // re-render, so use Playwright's auto-retrying assertion to poll for the new
        // text.
        await Assertions.Expect(_settingsPage.LocationPill)
            .ToContainTextAsync(placeName, new() { Timeout = 30000 });
    }

    [Then(@"I see the ""([^""]*)"" badge on the location pill")]
    public async Task ThenISeeTheBadgeOnTheLocationPill(string badgeText)
    {
        // The badge text transitions from "Auto" to "Saved" after the save request
        // completes and Blazor re-renders.  Use Playwright's auto-retrying assertion so
        // we poll for the expected text instead of reading InnerTextAsync once and
        // racing the re-render.
        await Assertions.Expect(_settingsPage.LocationPillBadge)
            .ToHaveTextAsync(badgeText, new() { Timeout = 10000 });
    }

    [Then(@"I see the Morning theme tile with a time")]
    public async Task ThenISeeTheMorningThemeTileWithATime()
    {
        await _settingsPage.MorningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.MorningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Morning tile should display a time");
    }

    [Then(@"I see the Daytime theme tile with a time")]
    public async Task ThenISeeTheDaytimeThemeTileWithATime()
    {
        await _settingsPage.DaytimeTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.DaytimeTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Daytime tile should display a time");
    }

    [Then(@"I see the Evening theme tile with a time")]
    public async Task ThenISeeTheEveningThemeTileWithATime()
    {
        await _settingsPage.EveningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.EveningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Evening tile should display a time");
    }

    [Then(@"I see the Night theme tile with a time")]
    public async Task ThenISeeTheNightThemeTileWithATime()
    {
        await _settingsPage.NightTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.NightTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Night tile should display a time");
    }

    [Then(@"I see the username in the account section")]
    public async Task ThenISeeTheUsernameInTheAccountSection()
    {
        await _settingsPage.AccountName.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var text = await _settingsPage.AccountName.InnerTextAsync();
        text.Trim().Should().NotBeEmpty("Account section should display the signed-in username");
    }

    [When(@"I disable auto-change theme")]
    public async Task WhenIDisableAutoChangeTheme()
    {
        var isChecked = await _settingsPage.AutoThemeToggle.IsCheckedAsync();
        if (isChecked)
        {
            await _settingsPage.AutoThemeToggle.ClickAsync();
            // Wait for the hint that confirms manual mode is active
            var page = _scenarioContext.Get<IPage>();
            await page.Locator(".settings-hint").Filter(new() { HasText = "Tap a theme to apply it instantly" })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
    }

    [When(@"I select the ""([^""]*)"" theme tile")]
    public async Task WhenISelectTheThemeTile(string themeName)
    {
        await _settingsPage.ThemeTile(themeName).ClickAsync();
    }

    [Then(@"the theme tiles are not selectable")]
    public async Task ThenTheThemeTilesAreNotSelectable()
    {
        var classes = await _settingsPage.MorningTile.GetAttributeAsync("class") ?? "";
        classes.Should().Contain("theme-tile--readonly",
            "tiles should be read-only when auto-change is enabled");
    }

    [Then(@"the settings tab in position (\d+) is ""([^""]*)""")]
    public async Task ThenTheSettingsTabInPositionIs(int position, string expectedLabel)
    {
        var page = _scenarioContext.Get<IPage>();
        // 1-based position over the settings tab strip
        var tab = page.Locator($".settings-tab-strip .settings-tab:nth-child({position})");
        await Assertions.Expect(tab).ToBeVisibleAsync(new() { Timeout = 10000 });
        var label = await tab.Locator(".settings-tab__label").InnerTextAsync();
        label.Trim().Should().Be(expectedLabel,
            $"settings tab in position {position} should be '{expectedLabel}'");
    }

    [Then(@"the ""([^""]*)"" theme tile is selected")]
    public async Task ThenTheThemeTileIsSelected(string themeName)
    {
        var classes = await _settingsPage.ThemeTile(themeName).GetAttributeAsync("class") ?? "";
        classes.Should().Contain("theme-tile--selected",
            $"the {themeName} tile should show as selected after being clicked");
    }
}
