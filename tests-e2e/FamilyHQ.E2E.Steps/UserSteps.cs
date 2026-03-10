using System;
using System.Threading.Tasks;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FamilyHQ.E2E.Steps.Hooks;
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
    public async Task GivenIHaveAUserLikeWithCalendar(string userName, string calendarName)
    {
        if (!TemplateHooks.UserTemplates.TryGetValue(userName, out var template))
        {
            throw new Exception($"Template '{userName}' not found in user_templates.json");
        }

        var isolatedTemplate = new SimulatorConfigurationModel();
        var newCalendarIds = new System.Collections.Generic.Dictionary<string, string>();

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
            throw new Exception($"Calendar '{calendarName}' not found in template '{userName}'.");
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

        _scenarioContext["UserName"] = userName;
        _scenarioContext["UserTemplate"] = isolatedTemplate;
        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }
}
