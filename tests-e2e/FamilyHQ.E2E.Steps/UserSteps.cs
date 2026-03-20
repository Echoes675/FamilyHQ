using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FamilyHQ.E2E.Steps.Hooks;
using Microsoft.Playwright;
using Reqnroll;

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

    [Given(@"I have a user like ""([^""]*)"" with calendar ""([^""]*)""")]
    public async Task GivenIHaveAUserLikeWithCalendar(string userKey, string calendarName)
    {
        if (!TemplateHooks.UserTemplates.TryGetValue(userKey, out var template))
        {
            throw new Exception($"Template '{userKey}' not found in user_templates.json");
        }

        // Generate a unique username per call so that each scenario execution is fully
        // isolated in both the simulator and the WebApi database, even when the same
        // userKey (e.g. "Test Family Member") appears in Background and in a Scenario.
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
                BackgroundColor = c.BackgroundColor
            });

            if (c.Summary == calendarName)
            {
                _scenarioContext["CurrentCalendarId"] = newId;
            }
        }

        if (!_scenarioContext.ContainsKey("CurrentCalendarId"))
        {
            throw new Exception($"Calendar '{calendarName}' not found in template '{userKey}'.");
        }

        foreach(var e in template.Events)
        {
            isolatedTemplate.Events.Add(new SimulatorEventModel
            {
                Id = "evt_" + Guid.NewGuid().ToString("N"),
                CalendarId = newCalendarIds.TryGetValue(e.CalendarId, out var mappedId) ? mappedId : e.CalendarId,
                Summary = e.Summary,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                IsAllDay = e.IsAllDay
            });
        }

        // Store by userKey so GivenILoginAsTheUser can look up the unique username.
        _scenarioContext[$"UniqueUsername:{userKey}"] = uniqueUsername;
        _scenarioContext["UserTemplate"] = isolatedTemplate;
        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"I login as the user ""([^""]*)""")]
    public async Task GivenILoginAsTheUser(string userKey)
    {
        var uniqueUsername = _scenarioContext.Get<string>($"UniqueUsername:{userKey}");

        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();

        // Navigate to the dashboard and wait for Blazor's auth check to complete
        await page.GotoAsync(config.BaseUrl + "/");
        await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Check if user is already signed in, and if so, sign out first
        var dashboardPage = new Common.Pages.DashboardPage(page);
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

        // Click Login to Google and follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        //
        // ClickAsync tracks the element handle and performs "scroll into view if needed"
        // before clicking. If Blazor's post-initialisation render cycle recreates the button
        // element between the scroll step and the click dispatch, Playwright detects the
        // detachment and retries indefinitely until the 30-second default timeout expires.
        //
        // Clicking via page.Mouse.ClickAsync(x, y) dispatches events at screen coordinates
        // rather than tracking a specific element handle. Even if the button is briefly
        // recreated by a Blazor render, the new element sits at the same screen position and
        // receives the click — avoiding the element-tracking race entirely.
        var loginBtn = page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });
        await loginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Obtain the button's screen position. BoundingBoxAsync may throw or return null if
        // the element is momentarily detached during a Blazor render; retry until stable.
        BoundingBox? box = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (box == null && DateTime.UtcNow < deadline)
        {
            try
            {
                box = await loginBtn.BoundingBoxAsync();
                if (box == null) await Task.Delay(50);
            }
            catch (PlaywrightException)
            {
                await Task.Delay(50);
            }
        }

        if (box == null)
            throw new InvalidOperationException("Login button bounding box unavailable after 10 seconds");

        await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });

        // Select the user on the simulator consent screen
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });
        await page.Locator("button[type='submit']").ClickAsync();

        // Wait for navigation to complete back to dashboard
        await page.WaitForURLAsync(config.BaseUrl + "/");
    }
}
