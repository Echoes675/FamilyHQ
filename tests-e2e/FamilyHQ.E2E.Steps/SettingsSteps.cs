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
        // Web-first: the tile time renders as the Display tab paints; ToHaveTextAsync with a
        // non-whitespace regex auto-retries against the live DOM rather than reading once (FHQ-41).
        await Assertions.Expect(_settingsPage.MorningTile.Locator(".theme-tile__time"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"\S"), new() { Timeout = 30000 });
    }

    [Then(@"I see the Daytime theme tile with a time")]
    public async Task ThenISeeTheDaytimeThemeTileWithATime()
    {
        await Assertions.Expect(_settingsPage.DaytimeTile.Locator(".theme-tile__time"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"\S"), new() { Timeout = 30000 });
    }

    [Then(@"I see the Evening theme tile with a time")]
    public async Task ThenISeeTheEveningThemeTileWithATime()
    {
        await Assertions.Expect(_settingsPage.EveningTile.Locator(".theme-tile__time"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"\S"), new() { Timeout = 30000 });
    }

    [Then(@"I see the Night theme tile with a time")]
    public async Task ThenISeeTheNightThemeTileWithATime()
    {
        await Assertions.Expect(_settingsPage.NightTile.Locator(".theme-tile__time"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"\S"), new() { Timeout = 30000 });
    }

    [Then(@"I see the username in the account section")]
    public async Task ThenISeeTheUsernameInTheAccountSection()
    {
        // Web-first: the account name renders once the user profile loads; ToHaveTextAsync with a
        // non-whitespace regex auto-retries against the live DOM rather than reading once (FHQ-41).
        await Assertions.Expect(_settingsPage.AccountName)
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"\S"), new() { Timeout = 30000 });
    }

    [When(@"I disable auto-change theme")]
    public async Task WhenIDisableAutoChangeTheme()
    {
        var isOn = await _settingsPage.IsAutoThemeOnAsync();
        if (isOn)
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
        // Web-first: the read-only class is applied when auto-change is enabled; ToHaveClassAsync
        // auto-retries against the live DOM rather than reading the class once (FHQ-41).
        await Assertions.Expect(_settingsPage.MorningTile)
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("theme-tile--readonly"),
                new() { Timeout = 30000 });
    }

    [Then(@"the settings tab in position (\d+) is ""([^""]*)""")]
    public async Task ThenTheSettingsTabInPositionIs(int position, string expectedLabel)
    {
        var page = _scenarioContext.Get<IPage>();
        // 1-based position over the settings tab strip
        var tab = page.Locator($".settings-tab-strip .settings-tab:nth-child({position})");
        await Assertions.Expect(tab).ToBeVisibleAsync(new() { Timeout = 10000 });
        // Web-first: the tab label text auto-retries against the live DOM rather than reading once (FHQ-41).
        await Assertions.Expect(tab.Locator(".settings-tab__label"))
            .ToHaveTextAsync(expectedLabel, new() { Timeout = 30000 });
    }

    [Then(@"the ""([^""]*)"" theme tile is selected")]
    public async Task ThenTheThemeTileIsSelected(string themeName)
    {
        // Web-first: the selected class is applied after the tile is tapped; ToHaveClassAsync
        // auto-retries against the live DOM rather than reading the class once (FHQ-41).
        await Assertions.Expect(_settingsPage.ThemeTile(themeName))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("theme-tile--selected"),
                new() { Timeout = 30000 });
    }
}
