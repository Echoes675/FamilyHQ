using FamilyHQ.E2E.Data.Api;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// FHQ-207: drives the Simulator backdoor that changes a calendar's default reminders, so CI can
/// reach the write path that caused the FHQ-205 production outage.
/// </summary>
[Binding]
public class CalendarDefaultRemindersSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public CalendarDefaultRemindersSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [When(@"the active calendar's default reminders change to (\d+) minutes in Google")]
    public async Task WhenTheActiveCalendarsDefaultRemindersChangeTo(int minutes)
    {
        // CurrentCalendarId is the scenario's own uniquely-suffixed id, set by
        // "the ""X"" calendar is the active calendar". Never hard-code an id: scenarios run in
        // parallel against one Simulator and must not touch each other's data.
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        await _simulatorApi.SetCalendarDefaultRemindersAsync(
            calendarId,
            new[] { new { method = "popup", minutes } });
    }

    [When(@"the active calendar's default reminders are cleared in Google")]
    public async Task WhenTheActiveCalendarsDefaultRemindersAreCleared()
    {
        var calendarId = _scenarioContext.GetCurrentCalendarId();
        await _simulatorApi.SetCalendarDefaultRemindersAsync(calendarId, null);
    }
}
