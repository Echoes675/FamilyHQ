using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FamilyHQ.E2E.Steps.Hooks;
using Microsoft.Playwright;
using Reqnroll;
using System.Linq;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class UserSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public UserSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [Given(@"I have a user like ""([^""]*)""")]
    public async Task GivenIHaveAUserLike(string userKey)
    {
        if (!TemplateHooks.UserTemplates.TryGetValue(userKey, out var template))
            throw new InvalidOperationException($"Template '{userKey}' not found in user_templates.json");

        var uniqueUsername = $"{userKey}_{Guid.NewGuid():N}";
        var isolatedTemplate = new SimulatorConfigurationModel { UserName = uniqueUsername };
        var newCalendarIds = new Dictionary<string, string>();

        foreach (var c in template.Calendars)
        {
            var newId = "cal_" + Guid.NewGuid().ToString("N");
            newCalendarIds[c.Id] = newId;

            isolatedTemplate.Calendars.Add(new SimulatorCalendarModel
            {
                Id = newId,
                Summary = c.Summary,
                BackgroundColor = c.BackgroundColor,
                IsShared = c.IsShared
            });
        }

        foreach (var e in template.Events)
        {
            isolatedTemplate.Events.Add(new SimulatorEventModel
            {
                Id = "evt_" + Guid.NewGuid().ToString("N"),
                CalendarId = newCalendarIds.TryGetValue(e.CalendarId, out var mappedId) ? mappedId : e.CalendarId,
                Summary = e.Summary,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                IsAllDay = e.IsAllDay,
                Description = e.Description
            });
        }

        // Store by userKey so GivenILoginAsTheUser can look up the unique username.
        _scenarioContext[$"UniqueUsername:{userKey}"] = uniqueUsername;
        _scenarioContext["UserTemplate"] = isolatedTemplate;
        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the ""([^""]*)"" calendar is the active calendar")]
    public Task GivenTheCalendarIsTheActiveCalendar(string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName);

        if (calendar == null)
            throw new InvalidOperationException(
                $"Calendar '{calendarName}' not found in the user template. " +
                $"Available: {string.Join(", ", isolatedTemplate.Calendars.Select(c => c.Summary))}");

        _scenarioContext["CurrentCalendarId"] = calendar.Id;
        return Task.CompletedTask;
    }

    [Given(@"I login as the user ""([^""]*)""")]
    public async Task GivenILoginAsTheUser(string userKey)
    {
        var uniqueUsername = _scenarioContext.Get<string>($"UniqueUsername:{userKey}");

        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        var dashboardPage = new Common.Pages.DashboardPage(page);

        // Navigate to the dashboard and wait for Blazor's auth check to complete
        await page.GotoAsync(config.BaseUrl + "/");
        await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Click Login to Google and follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        //
        // The click and the OAuth navigation must be retried as a unit.  Blazor can
        // re-render the login page between the click and the redirect (e.g. when the
        // dashboard is still transitioning out of a prior user's authenticated state),
        // causing the click event to be dispatched to a stale element and silently
        // dropped.  We use a short (15s) navigation timeout so a dropped click surfaces
        // quickly and the whole click + nav sequence can be retried — rather than
        // hanging on a single 120s wait.
        //
        // If Blazor re-authenticates from a stale localStorage token that survives across
        // the sign-out cycle, the Login button appears briefly (first render) then
        // disappears when OnInitializedAsync completes with _isAuthenticated=true. We
        // detect this case by checking whether the Sign Out button appeared, then sign
        // out and retry.
        var navigationOk = false;
        for (var attempt = 0; attempt < 3 && !navigationOk; attempt++)
        {
            // If signed in (from Background login or stale re-auth), sign out first
            if (await dashboardPage.IsSignedInAsync())
            {
                await dashboardPage.SignOutAsync();
                // Wipe cookies and storage so Blazor cannot re-authenticate on the next load.
                await page.Context.ClearCookiesAsync();
                await page.EvaluateAsync("() => { localStorage.clear(); sessionStorage.clear(); }");
                // Reload to a clean unauthenticated state and wait for Blazor auth check to settle.
                await page.GotoAsync(config.BaseUrl + "/");
                await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
            }

            var loginBtn = page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });

            // Wait for Login button to appear. If it times out, Blazor may have rendered into
            // authenticated state immediately — loop back to sign-out and retry.
            try
            {
                await loginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
            }
            catch (Exception)
            {
                continue; // sign-out check at top of loop will handle Sign Out state
            }

            // Click via the locator so Playwright auto-waits for actionability and
            // handles brief Blazor re-renders of the button itself.  Catch failures so
            // the loop can retry rather than throwing out of the method.
            try
            {
                await loginBtn.ClickAsync(new() { Timeout = 5000 });
            }
            catch (Exception)
            {
                continue;
            }

            // The click should trigger navigation to the simulator OAuth consent page.
            // Short timeout: if nav hasn't started within 15s the click was dropped
            // (Blazor re-rendered mid-dispatch), so reset to / and retry the cycle.
            try
            {
                await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
                navigationOk = true;
            }
            catch (TimeoutException)
            {
                await page.GotoAsync(config.BaseUrl + "/");
                await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
            }
        }

        if (!navigationOk)
            throw new InvalidOperationException("Login click failed to initiate OAuth navigation after 3 attempts");

        // Select the user on the simulator consent screen
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();

        // Wait for navigation to complete back to dashboard
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

        // If the template designates a shared calendar, configure it in the app while we are
        // already on the settings page.  This must happen after login/sync so the app has the
        // user's calendars in its DB; it is transparent to scenarios — no extra Given step needed.
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var sharedCalendar = isolatedTemplate.Calendars.FirstOrDefault(c => c.IsShared);
        if (sharedCalendar != null)
        {
            var settingsPage = new SettingsPage(page);
            await settingsPage.NavigateToCalendarsTabAsync();
            await settingsPage.DesignateSharedCalendarAsync(sharedCalendar.Summary);
            await page.Locator(".alert-success").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }

        // Return to the dashboard so subsequent steps start from a known state.
        await page.GotoAsync(config.BaseUrl + "/");
        await page.Locator(".dashboard-header__settings").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }
}
