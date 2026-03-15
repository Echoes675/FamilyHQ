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

        // Navigate to the OAuth consent screen
        await page.GotoAsync("https://localhost:7199/oauth2/auth?redirect_uri=" + config.ApiBaseUrl + "/api/auth/callback&client_id=test");

        // Select the user from the dropdown
        var userSelect = page.Locator("select#selectedUserId");
        await userSelect.SelectOptionAsync(new SelectOptionValue { Label = uniqueUsername });

        // Click the Continue button
        await page.Locator("button[type='submit']").ClickAsync();

        // Wait for navigation to complete
        await page.WaitForURLAsync(config.BaseUrl + "/");
    }
}
