using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WebhookDataSteps
{
    private const int LiveUpdateTimeoutMs = 5000;
    private const int LiveUpdatePollIntervalMs = 250;

    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;

    public WebhookDataSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    [When(@"a new event ""([^""]*)"" is added to Google Calendar")]
    public async Task WhenANewEventIsAddedToGoogleCalendar(string eventName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");
        var tomorrow = DateTime.Today.AddDays(1);

        var eventId = await _simulatorApi.AddEventAsync(
            userId: template.UserName,
            calendarId: calendarId,
            summary: eventName,
            start: tomorrow,
            end: tomorrow.AddDays(1),
            isAllDay: true);

        // Strip surrounding quotes that ReadAsStringAsync may include
        eventId = eventId.Trim('"');
        _scenarioContext[$"CreatedEventId:{eventName}"] = eventId;
    }

    [When(@"the event ""([^""]*)"" is updated to ""([^""]*)"" in Google Calendar")]
    public async Task WhenTheEventIsUpdatedInGoogleCalendar(string originalName, string newName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var eventId = ResolveEventId(originalName);

        await _simulatorApi.UpdateEventAsync(template.UserName, eventId, newName);
    }

    [When(@"the event ""([^""]*)"" is deleted from Google Calendar")]
    public async Task WhenTheEventIsDeletedFromGoogleCalendar(string eventName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var eventId = ResolveEventId(eventName);

        await _simulatorApi.DeleteEventAsync(template.UserName, eventId);
    }

    [When(@"Google Calendar sends a webhook notification")]
    public async Task WhenGoogleCalendarSendsAWebhookNotification()
    {
        await _simulatorApi.TriggerWebhookAsync();
    }

    [Then(@"the dashboard live-updates to show ""([^""]*)""")]
    public async Task ThenTheDashboardLiveUpdatesToShow(string eventName)
    {
        await WaitForConditionAsync(
            condition: async () =>
            {
                var events = await _dashboardPage.GetVisibleEventsAsync();
                return events.Any(e => e.Contains(eventName));
            },
            failMessage: $"Dashboard did not live-update within 5s after webhook notification (expected to show '{eventName}')");
    }

    [Then(@"the dashboard live-updates to remove ""([^""]*)""")]
    public async Task ThenTheDashboardLiveUpdatesToRemove(string eventName)
    {
        await WaitForConditionAsync(
            condition: async () =>
            {
                var events = await _dashboardPage.GetVisibleEventsAsync();
                return !events.Any(e => e.Contains(eventName));
            },
            failMessage: $"Dashboard did not live-update within 5s after webhook notification (expected to remove '{eventName}')");
    }

    // Resolves an event ID from ScenarioContext.
    // Checks for a name-keyed dynamic ID first (for dynamically-added events), then falls back
    // to searching the UserTemplate's Events list by Summary (for pre-seeded events).
    private string ResolveEventId(string eventName)
    {
        if (_scenarioContext.TryGetValue($"CreatedEventId:{eventName}", out var storedId) && storedId is string id)
            return id;

        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var match = template.Events.FirstOrDefault(e => e.Summary == eventName);
        if (match == null)
            throw new InvalidOperationException(
                $"Could not resolve event ID for '{eventName}'. " +
                "Ensure the event was seeded via 'Given the user has an all-day event' or added via 'When a new event \"{eventName}\" is added'.");

        return match.Id;
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition, string failMessage)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(LiveUpdateTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(LiveUpdatePollIntervalMs);
        }
        throw new TimeoutException(failMessage);
    }
}
