using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

using FamilyHQ.E2E.Common.Helpers;

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
        var calendarId = _scenarioContext.GetCurrentCalendarId();
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
        await WaitForSyncQueueToDrainAsync();
    }

    /// <summary>
    /// Restores the synchronous-sync timing contract these scenarios were written against.
    /// The webhook now ENQUEUES a durable CalendarSyncJob and acks immediately (FHQ-37); the
    /// single-consumer CalendarSyncWorker drains it a beat later, so without this barrier any
    /// immediately-following dashboard assertion races the worker (intermittent-issues #6).
    /// Polls the authenticated /api/diagnostics/sync-queue-depth (Bearer token read from
    /// localStorage) until the CURRENT user's Pending+InProgress jobs reach 0 — per-user so a
    /// parallel scenario's queue activity never blocks this one. Degrades gracefully: if the
    /// page has no auth context yet it does a brief settle and returns; if the queue never
    /// drains within the deadline it proceeds (downstream steps carry their own waits/polls).
    /// </summary>
    private async Task WaitForSyncQueueToDrainAsync()
    {
        var page = _scenarioContext.Get<IPage>();
        var deadline = System.DateTime.UtcNow.AddSeconds(40);
        while (System.DateTime.UtcNow < deadline)
        {
            var active = await page.EvaluateAsync<int>(@"
                async () => {
                    try {
                        const t = localStorage.getItem('familyhq_auth_token');
                        if (!t) return -2; // no auth context on this page yet
                        const r = await fetch('/api/diagnostics/sync-queue-depth', { headers: { 'Authorization': 'Bearer ' + t } });
                        if (r.status === 401) return -2;
                        if (!r.ok) return -1; // transient — keep polling
                        const b = await r.json();
                        return (b && typeof b.active === 'number') ? b.active : -1;
                    } catch (e) { return -1; }
                }");

            if (active == 0)
                return; // this user's queue has drained — sync applied

            if (active == -2)
            {
                // Not authenticated on this page — the barrier doesn't apply; brief settle then proceed.
                await Task.Delay(1500);
                return;
            }

            await Task.Delay(500);
        }
        // Deadline elapsed: proceed; downstream assertions have their own waits/polls.
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

    [When(@"a new event ""([^""]*)"" is added to Google Calendar on ""([^""]*)"" in ""([^""]*)""")]
    public async Task WhenANewEventIsAddedOnDateInCalendar(string eventName, string dateExpr, string calendarName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = template.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.ParseExact(DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var eventId = await _simulatorApi.AddEventAsync(
            userId: template.UserName,
            calendarId: calendar.Id,
            summary: eventName,
            start: date,
            end: date.AddDays(1),
            isAllDay: true);

        eventId = eventId.Trim('"');
        _scenarioContext[$"CreatedEventId:{eventName}"] = eventId;
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
