using System;
using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// FHQ-18.11 Pass 5 (§10.2): echo-guard scenarios for recurring writes. A single write on a series
/// (an "All events" PATCH, or a native series creation) produces ONE outbound write to Google; the
/// resulting fan-out webhooks for the expanded instances are suppressed by the self-echo guard
/// (the app records the echoed master content-hash per reconciled instance, FHQ-18.4).
///
/// Reuses the Simulator's in-memory <c>OutboundWriteCountStore</c> exposed via the existing
/// write-count backdoor (the same mechanism the single-event WebhookEchoGuard scenario uses):
///   • per-master count   — GET /api/simulator/backdoor/write-counts/{eventId}
///   • total across all   — GET /api/simulator/backdoor/write-counts/total (used when the master's
///                          ID is generated server-side, i.e. native series creation).
/// Counts are reset before/after each scenario by <see cref="Hooks.WebhookEchoGuardHooks"/> (tagged).
/// </summary>
[Binding]
public class RecurringEchoGuardSteps
{
    /// <summary>
    /// Fixed settle period after triggering the echo webhook, mirroring the single-event guard step.
    /// We are asserting the ABSENCE of a second write, so there is no positive condition to poll for.
    /// </summary>
    private const int WebhookSettleMs = 5000;

    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;

    public RecurringEchoGuardSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    // ── When ───────────────────────────────────────────────────────────────────

    // Edits the first occurrence of the tracked series at "All events" scope, renaming it. The app
    // PATCHes the master once and reconciles the window; the fan-out echo webhooks for the touched
    // instances are suppressed by the guard. Reuses the Pass-3 scope driver via the page object.
    [When(@"I rename the recurring series ""([^""]*)"" to ""([^""]*)"" applying to all events")]
    public async Task WhenIRenameTheRecurringSeriesApplyingToAllEvents(string seriesName, string newTitle)
    {
        var first = _scenarioContext.Get<DateTime>("RecurringSeriesFirstOccurrenceDate");
        await _dashboardPage.EditRecurringOccurrenceTitleWithScopeAsync(seriesName, first, newTitle, "all");
    }

    // Creates a weekly recurring series natively through the FamilyHQ create modal. The app inserts
    // the master once and reconciles; the expansion's echo webhooks are suppressed by the guard.
    [When(@"I create a weekly recurring event ""([^""]*)"" in ""([^""]*)"" tracking outbound writes")]
    public async Task WhenICreateAWeeklyRecurringEventTrackingOutboundWrites(string eventName, string calendarName)
    {
        await _dashboardPage.CreateWeeklyRecurringEventAsync(eventName, calendarName);
        _scenarioContext["RecurringSeriesFirstOccurrenceDate"] = DateTime.Today;
    }

    // Triggers an explicit webhook so the Simulator fires the echo path in the WebApi, then waits the
    // fixed settle period for the full async sync round-trip to complete before asserting counts.
    [When(@"I wait for the recurring fan-out webhooks to be processed")]
    public async Task WhenIWaitForTheRecurringFanOutWebhooksToBeProcessed()
    {
        await _simulatorApi.TriggerWebhookAsync();
        await Task.Delay(WebhookSettleMs);
    }

    // ── Then ───────────────────────────────────────────────────────────────────

    // Asserts the series master received exactly one outbound write. A count of 2+ means an echo
    // webhook was NOT suppressed and triggered a second write back to Google.
    [Then(@"exactly one outbound write to Google is recorded for the series")]
    public async Task ThenExactlyOneOutboundWriteToGoogleIsRecordedForTheSeries()
    {
        var masterId = _scenarioContext.Get<string>("RecurringSeriesMasterId");
        var writeCount = await _simulatorApi.GetOutboundWriteCountAsync(masterId);

        writeCount.Should().Be(1,
            $"the self-echo guard must suppress the fan-out echo webhooks so exactly one outbound " +
            $"write reaches Google for the series master '{masterId}'. A count of 2+ means an echo " +
            "was not suppressed and triggered a second write.");
    }

    // Asserts the Simulator recorded exactly one outbound write across all events. Used for native
    // series creation, where the master's Google ID is generated server-side and unknown to the test:
    // a single insert means total == 1; a suppressed-echo failure would push the total to 2+.
    [Then(@"exactly one outbound write to Google is recorded in total")]
    public async Task ThenExactlyOneOutboundWriteToGoogleIsRecordedInTotal()
    {
        var total = await _simulatorApi.GetTotalOutboundWriteCountAsync();

        total.Should().Be(1,
            "creating a recurring series natively performs exactly one outbound insert; the " +
            "expansion's echo webhooks must be suppressed by the self-echo guard. A total of 2+ " +
            "means an echo was not suppressed and triggered a write back to Google.");
    }
}
