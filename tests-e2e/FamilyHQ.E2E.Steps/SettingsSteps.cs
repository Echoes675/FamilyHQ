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
        await Assertions.Expect(_settingsPage.AccountName).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [When(@"I navigate to the settings page")]
    public async Task WhenINavigateToTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [Then(@"I see the location pill displaying ""([^""]*)""")]
    public async Task ThenISeeTheLocationPillDisplaying(string placeName)
    {
        await _settingsPage.LocationPill.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var text = await _settingsPage.LocationPill.InnerTextAsync();
        text.Should().Contain(placeName);
    }

    [Then(@"I see the ""([^""]*)"" badge on the location pill")]
    public async Task ThenISeeTheBadgeOnTheLocationPill(string badgeText)
    {
        var text = await _settingsPage.LocationPillBadge.InnerTextAsync();
        text.Trim().Should().Be(badgeText);
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
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var text = await _settingsPage.AccountName.InnerTextAsync();
        text.Trim().Should().NotBeEmpty("Account section should display the signed-in username");
    }
}
