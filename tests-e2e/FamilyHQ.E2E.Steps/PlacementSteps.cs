using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// FHQ-68: asserts which Google calendar a synced event is stored on in the Simulator, to verify that a
// multi-attendee event created directly in Google on a personal calendar is migrated to the shared
// calendar (and written back to Google), while a single-attendee event is left on its personal calendar.
[Binding]
public class PlacementSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public PlacementSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [Then(@"the event ""([^""]*)"" is on the ""([^""]*)"" calendar")]
    public async Task ThenTheEventIsOnTheCalendar(string summary, string calendarName)
    {
        var (userId, calendarId) = Resolve(calendarName);
        var found = false;
        await Polling.UntilAsync(
            async () => found = await _simulatorApi.EventExistsOnCalendarAsync(userId, calendarId, summary),
            failMessage: $"Event '{summary}' was never found on calendar '{calendarName}' ({calendarId}) for user '{userId}'.",
            timeoutMs: 30000, intervalMs: 500);
        found.Should().BeTrue();
    }

    [Then(@"the event ""([^""]*)"" is not on the ""([^""]*)"" calendar")]
    public async Task ThenTheEventIsNotOnTheCalendar(string summary, string calendarName)
    {
        var (userId, calendarId) = Resolve(calendarName);
        // The event may briefly still exist on the source calendar mid-migration; poll until it is gone.
        var present = true;
        await Polling.UntilAsync(
            async () => !(present = await _simulatorApi.EventExistsOnCalendarAsync(userId, calendarId, summary)),
            failMessage: $"Event '{summary}' is still present on calendar '{calendarName}' ({calendarId}) for user '{userId}' (expected it to have moved off).",
            timeoutMs: 30000, intervalMs: 500);
        present.Should().BeFalse();
    }

    private (string userId, string calendarId) Resolve(string calendarName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = template.Calendars.Find(c => c.Summary == calendarName)
            ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found in template.");
        return (template.UserName, calendar.Id);
    }
}
