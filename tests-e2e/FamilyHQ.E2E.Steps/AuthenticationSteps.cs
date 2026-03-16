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

        // Need to sign in
        await page.GotoAsync("https://localhost:7199/oauth2/auth?redirect_uri=" + config.ApiBaseUrl + "/api/auth/callback&client_id=test");
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(config.BaseUrl + "/");
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

        // Click the login button if visible
        if (await _dashboardPage.LoginBtn.CountAsync() > 0)
        {
            await _dashboardPage.LoginBtn.ClickAsync();
            
            // Wait for the login modal
            var loginModal = page.Locator(".login-modal-content");
            await loginModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
            
            var inputList = loginModal.GetByPlaceholder("e.g. Test Family Member");
            await inputList.FillAsync(uniqueUsername);
            
            await page.GetByRole(AriaRole.Button, new() { Name = "Simulate OAuth & Proceed" }).ClickAsync();
        }
        else
        {
            // Navigate directly to OAuth
            await page.GotoAsync("https://localhost:7199/oauth2/auth?redirect_uri=" + config.ApiBaseUrl + "/api/auth/callback&client_id=test");
            var userSelect = page.Locator("select#selectedUserId");
            await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
            await page.Locator("button[type='submit']").ClickAsync();
        }

        // Wait for navigation back to dashboard
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
