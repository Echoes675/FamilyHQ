using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Steps for the WebhookEchoGuard.feature scenarios (FHQ-30).
/// Proves the self-echo guard in CalendarSyncService prevents outbound writes
/// from triggering infinite write-webhook-write loops.
///
/// Write-count assertions use the Simulator's in-memory OutboundWriteCountStore,
/// exposed via the backdoor endpoint GET /api/simulator/backdoor/write-counts/{eventId}.
/// Cleanup runs in <see cref="Hooks.WebhookEchoGuardHooks"/> after each scenario.
/// </summary>
[Binding]
public class WebhookEchoGuardSteps
{
    /// <summary>
    /// How long to wait after triggering a webhook before asserting write counts.
    /// The sync pipeline is async: we must allow the WebApi to receive the webhook,
    /// run SyncAsync, check the hash cache, and skip or apply the event — then return.
    /// </summary>
    private const int WebhookSettleMs = 5000;

    /// <summary>
    /// Polling interval when waiting for live-update conditions.
    /// </summary>
    private const int PollIntervalMs = 250;

    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;

    public WebhookEchoGuardSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    // ── Given ──────────────────────────────────────────────────────────────────

    // All Given steps are provided by existing bindings:
    //   UserSteps:  "I have a user like X", "the X calendar is the active calendar", "I login as the user X"
    //   EventSteps: "the user has an all-day event X tomorrow"
    //   DashboardSteps: "I view the dashboard"

    // ── When ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the named event in the event modal and saves a new title.
    /// The original event name is resolved from the UserTemplate so the step does
    /// not depend on how the event was seeded. The Google event ID (for write-count
    /// assertions) is looked up from the UserTemplate and stashed in ScenarioContext.
    /// </summary>
    [When(@"I update the event title to ""([^""]*)""")]
    public async Task WhenIUpdateTheEventTitleTo(string newTitle)
    {
        // Locate the current (pre-update) event by scanning the template.
        // ScenarioContext["EchoGuardEventName"] is set by the first call to this step
        // within a scenario; if it is absent we derive the original name from the
        // most-recently seeded event.
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");

        string originalTitle;
        if (_scenarioContext.TryGetValue("EchoGuardEventName", out var stored) && stored is string cachedName)
        {
            originalTitle = cachedName;
        }
        else
        {
            // Pick the most recently added event in the template.
            var lastEvent = template.Events.LastOrDefault()
                ?? throw new InvalidOperationException(
                    "No events found in UserTemplate. Seed an event before calling 'When I update the event title to'.");
            originalTitle = lastEvent.Summary;
        }

        // Stash the Google event ID so assertion steps can query the write count.
        var templateEvent = template.Events.Find(e => e.Summary == originalTitle)
            ?? throw new InvalidOperationException(
                $"Event '{originalTitle}' not found in UserTemplate. " +
                "Ensure it was seeded via 'Given the user has an all-day event'.");

        _scenarioContext["EchoGuardEventId"] = templateEvent.Id;
        _scenarioContext["EchoGuardEventName"] = newTitle; // updated name for subsequent steps

        // FHQ-29 mitigation: wait for the event capsule to be visible before clicking.
        var capsule = _dashboardPage.EventCapsules.Filter(new() { HasText = originalTitle }).First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        await _dashboardPage.UpdateEventAsync(originalTitle, newTitle);
    }

    /// <summary>
    /// Waits for the echo-guard settle period — long enough for the WebApi to process
    /// any follow-up webhook and for SignalR to push any UI update. This is intentionally
    /// a fixed wait rather than a polling loop because the absence of a second write is
    /// what we are asserting; there is no positive condition to poll for.
    /// </summary>
    [When(@"I wait for any follow-up webhooks to be processed")]
    public async Task WhenIWaitForAnyFollowUpWebhooksToBeProcessed()
    {
        // Trigger an explicit webhook so the Simulator fires the echo path in the WebApi.
        await _simulatorApi.TriggerWebhookAsync();

        // Allow the full async round-trip to complete before asserting write counts.
        await Task.Delay(WebhookSettleMs);
    }

    /// <summary>
    /// Updates the event title directly in the Simulator (simulating a Google-side
    /// edit in the mobile app or web UI) without going through FamilyHQ.
    /// The event is resolved from the UserTemplate by its current name.
    /// </summary>
    [When(@"the event title is updated directly in Google to ""([^""]*)""")]
    public async Task WhenTheEventTitleIsUpdatedDirectlyInGoogleTo(string newTitle)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");

        // Resolve the event from the UserTemplate — last seeded event is the target.
        var templateEvent = template.Events.LastOrDefault()
            ?? throw new InvalidOperationException(
                "No events found in UserTemplate. Seed an event before updating directly in Google.");

        _scenarioContext["EchoGuardEventId"] = templateEvent.Id;

        await _simulatorApi.UpdateEventAsync(template.UserName, templateEvent.Id, newTitle);
    }

    /// <summary>
    /// Fires a webhook to the WebApi via the Simulator with the test-only delay header
    /// injected. The Simulator forwards the header to the WebApi, which delays processing
    /// by the given number of seconds (DEBUG-gated branch in SyncController). By the
    /// time the sync runs, the 60-second TTL in OutboundWriteHashCache has expired and
    /// the echo is NOT suppressed — proving stale echoes flow through normally.
    ///
    /// NOTE: This step incurs a real wall-clock wait equal to delaySeconds. It is tagged
    /// @Slow and should only be run in environments where long test durations are acceptable.
    /// </summary>
    [When(@"the echo webhook is delayed by (\d+) seconds via the test delay header")]
    public async Task WhenTheEchoWebhookIsDelayedBySecondsViaTestDelayHeader(int delaySeconds)
    {
        // Ensure the event ID is stashed (should have been set by the preceding When step).
        if (!_scenarioContext.TryGetValue("EchoGuardEventId", out _))
        {
            var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
            var templateEvent = template.Events.LastOrDefault()
                ?? throw new InvalidOperationException("No events in UserTemplate.");
            _scenarioContext["EchoGuardEventId"] = templateEvent.Id;
        }

        // TriggerDelayedWebhookAsync sends the delay header to the Simulator, which
        // forwards it to the WebApi. The HttpClient here waits for the full response,
        // so this call blocks for approximately delaySeconds seconds. That is intentional
        // — the xUnit test timeout (default 5 min) is well above 65s.
        await _simulatorApi.TriggerDelayedWebhookAsync(delaySeconds);

        // Brief settle after the delayed webhook returns.
        await Task.Delay(1000);
    }

    // ── Then ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that the Simulator received exactly one outbound write (PUT) for the
    /// event that was edited via FamilyHQ. A second write would indicate the echo
    /// webhook was NOT suppressed and triggered a re-sync that wrote back to Google.
    /// </summary>
    [Then(@"exactly one outbound write to Google has been recorded for the event")]
    public async Task ThenExactlyOneOutboundWriteToGoogleHasBeenRecordedForTheEvent()
    {
        var eventId = _scenarioContext.Get<string>("EchoGuardEventId");

        var writeCount = await _simulatorApi.GetOutboundWriteCountAsync(eventId);

        writeCount.Should().Be(1,
            $"the self-echo guard must suppress the echo webhook so exactly one outbound " +
            $"write reaches Google for event '{eventId}'. " +
            $"A count of 2+ means the echo was NOT suppressed and triggered a second write.");
    }

    /// <summary>
    /// Asserts the event still shows the updated title on the dashboard — confirming the
    /// echo-suppressed sync did not revert the title back to the pre-edit state.
    /// </summary>
    [Then(@"the event still shows the updated title ""([^""]*)"" on the dashboard")]
    public async Task ThenTheEventStillShowsTheUpdatedTitleOnTheDashboard(string expectedTitle)
    {
        // FHQ-29 mitigation: wait for the capsule to be visible using Playwright's built-in retry.
        var capsule = _dashboardPage.EventCapsules.Filter(new() { HasText = expectedTitle }).First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        (await _dashboardPage.GetVisibleEventsAsync())
            .Should().Contain(t => t.Contains(expectedTitle),
                $"the dashboard must show '{expectedTitle}' after the FamilyHQ edit, " +
                "meaning the echo suppression did not cause a revert");
    }

    /// <summary>
    /// Waits for the dashboard to display the specified title after an external Google-side
    /// edit flows through via webhook.
    /// </summary>
    [Then(@"the dashboard shows the updated title ""([^""]*)""")]
    public async Task ThenTheDashboardShowsTheUpdatedTitle(string expectedTitle)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(WebhookSettleMs);
        while (DateTime.UtcNow < deadline)
        {
            var events = await _dashboardPage.GetVisibleEventsAsync();
            if (events.Any(e => e.Contains(expectedTitle)))
                return;
            await Task.Delay(PollIntervalMs);
        }

        throw new TimeoutException(
            $"Dashboard did not show '{expectedTitle}' within {WebhookSettleMs}ms after webhook. " +
            "The Google-side edit may not have been processed (echo guard incorrectly suppressed it).");
    }

    /// <summary>
    /// After a delayed echo webhook (TTL expired), asserts the event title is visible on
    /// the dashboard (reconciled) and the Simulator received no second outbound write
    /// (the WebApi applied the inbound Google data without writing back to Google again).
    /// </summary>
    [Then(@"the event title is reconciled on the dashboard without a second outbound write")]
    public async Task ThenTheEventTitleIsReconciledOnTheDashboardWithoutASecondOutboundWrite()
    {
        // The delayed webhook should have caused the sync to process the event
        // because the TTL expired before the sync ran.
        // The event is in Google with the FamilyHQ-updated title, so reconciliation
        // leaves the title unchanged — verify it is still visible on the dashboard.
        var eventName = _scenarioContext.TryGetValue("EchoGuardEventName", out var stored) && stored is string n
            ? n
            : throw new InvalidOperationException("EchoGuardEventName not set in ScenarioContext.");

        var capsule = _dashboardPage.EventCapsules.Filter(new() { HasText = eventName }).First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // Verify no second outbound write was triggered by reconciliation.
        var eventId = _scenarioContext.Get<string>("EchoGuardEventId");
        var writeCount = await _simulatorApi.GetOutboundWriteCountAsync(eventId);

        writeCount.Should().Be(1,
            $"reconciling a stale echo must not trigger a second outbound write to Google " +
            $"for event '{eventId}'. A count of 2+ means the reconciliation incorrectly wrote back.");
    }
}
