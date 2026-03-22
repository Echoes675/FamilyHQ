using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class AuthenticationSteps
{
    private readonly DashboardPage _dashboardPage;
    private readonly ScenarioContext _scenarioContext;

    public AuthenticationSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(page);
    }

    [Given(@"I am not authenticated")]
    public async Task GivenIAmNotAuthenticated()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        
        // Navigate to the dashboard
        await page.GotoAsync(config.BaseUrl + "/");
        
        // If already signed in, sign out first
        if (await _dashboardPage.IsSignedInAsync())
        {
            await _dashboardPage.SignOutAsync();
        }
    }

    [Given(@"I am signed in as the user ""([^""]*)""")]
    public async Task GivenIAmSignedInAsTheUser(string userKey)
    {
        // This step requires the user to be set up first
        // Use the existing UserSteps to handle this
        var uniqueUsername = _scenarioContext.Get<string>($"UniqueUsername:{userKey}");
        
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();

        // Navigate to the dashboard
        await page.GotoAsync(config.BaseUrl + "/");

        // Check if already signed in with the correct user
        if (await _dashboardPage.IsSignedInAsync())
        {
            // Already signed in, we're good
            return;
        }

        // Need to sign in - click Login to Google and follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        await page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(config.BaseUrl + "/");

        // Wait for Blazor's auth check to complete and render the authenticated view
        // so that subsequent steps (e.g. clicking Sign Out) find the expected UI.
        await _dashboardPage.SignOutBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }

    [When(@"I navigate to the dashboard")]
    public async Task WhenINavigateToTheDashboard()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/");
    }

    [When(@"I sign in as the user ""([^""]*)""")]
    public async Task WhenISignInAsTheUser(string userKey)
    {
        var uniqueUsername = _scenarioContext.Get<string>($"UniqueUsername:{userKey}");
        
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();

        // Click Login to Google and follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        await _dashboardPage.LoginBtn.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(config.BaseUrl + "/");
    }

    [When(@"I click the ""([^""]*)"" button")]
    public async Task WhenIClickTheButton(string buttonName)
    {
        await _dashboardPage.SignOutAsync();
    }

    [Then(@"I see the ""([^""]*)"" button")]
    public async Task ThenISeeTheButton(string buttonName)
    {
        if (buttonName == "Login to Google")
        {
            await _dashboardPage.LoginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        else if (buttonName == "Sign Out")
        {
            await _dashboardPage.SignOutBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
    }

    [Then(@"I see the username displayed")]
    public async Task ThenISeeTheUsernameDisplayed()
    {
        await _dashboardPage.UserInfo.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Then(@"I do not see the username displayed")]
    public async Task ThenIDoNotSeeTheUsernameDisplayed()
    {
        await _dashboardPage.UserInfo.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        var userInfoVisible = await _dashboardPage.UserInfo.CountAsync() > 0;
        userInfoVisible.Should().BeFalse("Username should not be displayed after sign out");
    }

    [Then(@"I do not see the calendar")]
    public async Task ThenIDoNotSeeTheCalendar()
    {
        var calendarVisible = await _dashboardPage.MonthTable.CountAsync() > 0;
        calendarVisible.Should().BeFalse("Calendar should not be visible when not authenticated");
    }

    [Then(@"I see the calendar displayed")]
    public async Task ThenISeeTheCalendarDisplayed()
    {
        await _dashboardPage.MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
