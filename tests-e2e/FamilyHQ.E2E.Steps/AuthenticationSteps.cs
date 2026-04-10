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
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Check if already signed in with the correct user
        if (await _dashboardPage.IsSignedInAsync())
        {
            // Already signed in, we're good
            return;
        }

        // Click Login to Google and follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        //
        // The click and OAuth navigation must be retried as a unit. Blazor can re-render
        // the login page between the click and the redirect, causing the click to be
        // dispatched to a stale element and silently dropped. A short (15s) per-attempt
        // navigation timeout surfaces a dropped click quickly so the cycle can retry,
        // rather than hanging on a single 30s wait that fails the whole scenario.
        var navigationOk = false;
        for (var attempt = 0; attempt < 3 && !navigationOk; attempt++)
        {
            var loginBtn = page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });
            try
            {
                await loginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            }
            catch (Exception)
            {
                continue;
            }

            try
            {
                await loginBtn.ClickAsync(new() { Timeout = 5000 });
            }
            catch (Exception)
            {
                continue;
            }

            try
            {
                await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
                navigationOk = true;
            }
            catch (TimeoutException)
            {
                await page.GotoAsync(config.BaseUrl + "/");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
        }

        if (!navigationOk)
            throw new InvalidOperationException("Login click failed to initiate OAuth navigation after 3 attempts");

        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(config.BaseUrl + "/");

        // Verify authentication completed and the correct user is logged in:
        // wait for the settings gear (only rendered when authenticated), then navigate to
        // the settings page and confirm the account name matches the expected user.
        await page.Locator(".dashboard-header__settings").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.Locator(".account-name").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var displayedName = await page.Locator(".account-name").InnerTextAsync();
        if (!displayedName.Contains(uniqueUsername))
            throw new InvalidOperationException(
                $"Login verification failed: expected username '{uniqueUsername}' but got '{displayedName}'");

        // Return to the dashboard so subsequent steps start from a known state.
        await page.GotoAsync(config.BaseUrl + "/");
        await page.Locator(".dashboard-header__settings").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
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
        // Retry click + nav as a unit — Blazor can re-render the page between the click
        // and the redirect, causing the click to be dropped. See GivenIAmSignedInAsTheUser
        // for the full rationale.
        var navigationOk = false;
        for (var attempt = 0; attempt < 3 && !navigationOk; attempt++)
        {
            try
            {
                await _dashboardPage.LoginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
                await _dashboardPage.LoginBtn.ClickAsync(new() { Timeout = 5000 });
            }
            catch (Exception)
            {
                continue;
            }

            try
            {
                await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
                navigationOk = true;
            }
            catch (TimeoutException)
            {
                await page.GotoAsync(config.BaseUrl + "/");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
        }

        if (!navigationOk)
            throw new InvalidOperationException("Login click failed to initiate OAuth navigation after 3 attempts");

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
            // Sign Out button lives on the Settings page
            var page = _scenarioContext.Get<IPage>();
            var config = ConfigurationLoader.Load();
            await page.GotoAsync(config.BaseUrl + "/settings");
            await _dashboardPage.SignOutBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
    }

    [Then(@"I see the username displayed")]
    public async Task ThenISeeTheUsernameDisplayed()
    {
        // Username is displayed in the Account section on the Settings page
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.Locator(".account-name").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    [Then(@"I do not see the username displayed")]
    public async Task ThenIDoNotSeeTheUsernameDisplayed()
    {
        // After sign-out the user is on the home page; the dashboard-header is not rendered
        var signedIn = await _dashboardPage.IsSignedInAsync();
        signedIn.Should().BeFalse("User should not be signed in after sign out");
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
