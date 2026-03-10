using System;
using System.Threading.Tasks;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class EventSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public EventSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [Given(@"the user has an all-day event ""([^""]*)"" tomorrow")]
    public async Task GivenTheUserHasAnAll_DayEventTomorrow(string eventName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");
        
        var tomorrow = DateTime.Today.AddDays(1);
        
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = tomorrow,
            EndTime = tomorrow.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }
}
