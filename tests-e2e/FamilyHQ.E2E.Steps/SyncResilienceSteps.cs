using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Steps that drive the FHQ-25 (Google reauth resilience) and FHQ-26
/// (per-event resilience + diagnostics page) scenarios. Failure modes are
/// injected into the Simulator via the back-door endpoints and reset by
/// <see cref="Hooks.SyncResilienceHooks"/> after each scenario.
/// </summary>
[Binding]
public class SyncResilienceSteps
{
    /// <summary>
    /// Maximum time to wait for the CalendarSyncWorker to drain the enqueued job and
    /// record the terminally-failed run. Kept generous to absorb the worker's wake
    /// latency under the parallel E2E runner.
    /// </summary>
    private const int WaitForFailedRunSeconds = 40;

    /// <summary>
    /// Interval between polls of the failed-sync-runs endpoint while waiting for the
    /// worker to record the run.
    /// </summary>
    private const int WaitForFailedRunPollIntervalMs = 1000;

    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;
    private readonly DiagnosticsPage _diagnosticsPage;
    private readonly SettingsPage _settingsPage;
    private readonly TestConfiguration _config;

    public SyncResilienceSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        var page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(page);
        _diagnosticsPage = new DiagnosticsPage(page);
        _settingsPage = new SettingsPage(page);
        _config = ConfigurationLoader.Load();
    }

    [Given(@"a sync event failure has been recorded")]
    public async Task GivenASyncEventFailureHasBeenRecorded()
    {
        var userId = GetUserId();
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        await _simulatorApi.AddPoisonEventAsync(userId, calendarId);
        await TriggerManualSyncAsync();
    }

    [When(@"I view the diagnostics page")]
    public Task WhenIViewTheDiagnosticsPage() => _diagnosticsPage.GotoAsync();

    /// <summary>
    /// Polls the WebApi's /api/diagnostics/failed-sync-runs endpoint (via the page's
    /// authenticated fetch) until it reports at least one failed run for the current
    /// user, or the deadline elapses. The webhook now ENQUEUES a durable CalendarSyncJob
    /// and acks immediately; the CalendarSyncWorker drains the queue on its own cadence,
    /// runs the sync, and on the injected GoogleReauthRequiredException marks the run
    /// terminally Failed on the first attempt. This step gives the worker time to wake
    /// and process the job before the diagnostics assertion runs.
    /// </summary>
    [When(@"I wait for the failed sync run to be recorded")]
    public async Task WhenIWaitForTheFailedSyncRunToBeRecorded()
    {
        var page = _scenarioContext.Get<IPage>();

        // Land on an app page so the authenticated fetch carries the auth cookie and
        // resolves the API origin correctly.
        await page.GotoAsync(_config.BaseUrl + "/", new() { WaitUntil = WaitUntilState.NetworkIdle });

        var deadline = DateTime.UtcNow.AddSeconds(WaitForFailedRunSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var count = await page.EvaluateAsync<int>(@"
                async () => {
                    try {
                        const r = await fetch('/api/diagnostics/failed-sync-runs?limit=10', { credentials: 'include' });
                        if (!r.ok) return -1;
                        const body = await r.json();
                        return Array.isArray(body) ? body.length : 0;
                    } catch (e) {
                        return -1;
                    }
                }");

            if (count >= 1)
                return;

            await Task.Delay(WaitForFailedRunPollIntervalMs);
        }

        throw new TimeoutException(
            $"No failed sync run was recorded within {WaitForFailedRunSeconds}s after the webhook notification. " +
            "Expected the CalendarSyncWorker to drain the enqueued job and mark the reauth-failing run terminally Failed.");
    }

    [Then(@"I see the failed run in the recent failed sync runs table")]
    public async Task ThenISeeTheFailedRunInTheRecentFailedSyncRunsTable()
    {
        (await _diagnosticsPage.RunsEmptyState.IsVisibleAsync()).Should().BeFalse(
            "the runs empty-state placeholder must give way to the runs table once a terminally-failed sync run is recorded");

        var rows = await _diagnosticsPage.GetFailedRunRowCountAsync();
        rows.Should().BeGreaterThanOrEqualTo(1,
            "the recent failed sync runs table must contain at least one row for the webhook-driven sync that failed terminally");
    }

    [Then(@"I see the failure in the recent sync failures table")]
    public async Task ThenISeeTheFailureInTheRecentSyncFailuresTable()
    {
        (await _diagnosticsPage.IsEmptyStateVisibleAsync()).Should().BeFalse(
            "the empty-state placeholder must give way to the failures table once a per-event failure is recorded");

        var rows = await _diagnosticsPage.GetFailureRowCountAsync();
        rows.Should().BeGreaterThanOrEqualTo(1,
            "the recent sync failures table must contain at least one row for the poisoned event");
    }

    [Then(@"my other events still appear on the dashboard")]
    public async Task ThenMyOtherEventsStillAppearOnTheDashboard()
    {
        var page = _scenarioContext.Get<IPage>();
        await page.GotoAsync(_config.BaseUrl + "/");
        await _dashboardPage.WaitForCalendarToLoadAsync();

        var titles = await _dashboardPage.GetVisibleEventsAsync();
        titles.Any(t => t.Contains("Soccer practice")).Should().BeTrue(
            "the legitimate event seeded alongside the poisoned event must still sync to the dashboard");
    }

    private async Task<string> CollectReauthDiagnosticAsync(IPage page)
    {
        // Returns a single-line summary that callers embed in their assertion's
        // `because` clause so it surfaces in the xUnit Error Message. Earlier
        // versions wrote to Console.Error, which is NOT captured by xUnit's
        // per-test Standard Output, so the diagnostic was effectively silent.

        var syncStatus = _scenarioContext.TryGetValue("LastSyncResponseStatus", out int s)
            ? s.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "(not captured)";

        string apiResp;
        try
        {
            apiResp = await page.EvaluateAsync<string>(@"
                async () => {
                    try {
                        const r = await fetch('/api/calendars/connection-status', { credentials: 'include' });
                        const body = await r.text();
                        return JSON.stringify({ status: r.status, body });
                    } catch (e) {
                        return JSON.stringify({ error: String(e) });
                    }
                }");
        }
        catch (Exception ex)
        {
            apiResp = $"(fetch failed: {ex.Message})";
        }

        string pageState;
        try
        {
            pageState = await page.EvaluateAsync<string>(@"
                () => JSON.stringify({
                    url: window.location.href,
                    title: document.title,
                    hasReauthBanner: !!document.querySelector('[data-testid=""reauth-banner-dashboard""]'),
                    hasLoginButton: !!document.querySelector('button.btn-primary.btn-lg'),
                    bodyTextHead: document.body.innerText.substring(0, 500)
                })");
        }
        catch (Exception ex)
        {
            pageState = $"(eval failed: {ex.Message})";
        }

        return $"[FHQ-28 diagnostic] sync-response-status={syncStatus} | connection-status={apiResp} | page={pageState}";
    }

    private string GetUserId()
    {
        // The simulator keys data by config.UserName (set by UserSteps as
        // "{templateKey}_{guid}"). The same value is the per-user key for the
        // sync failure store.
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        return template.UserName;
    }

    private async Task TriggerManualSyncAsync()
    {
        await _settingsPage.NavigateToCalendarsTabAsync();
        var status = await _settingsPage.ClickSyncNowAsync();
        // FHQ-28: stash the WebApi's response status so failure-handling steps
        // can dump it. 409 = reauth correctly rejected. 200 = sync silently
        // succeeded (the failure mode never fired — points at WebApi).
        _scenarioContext["LastSyncResponseStatus"] = status;
    }

    [Given(@"the user's Google refresh token has been revoked")]
    public async Task GivenRefreshTokenRevoked()
    {
        await _simulatorApi.SetSyncFailureModeAsync(GetUserId(), "RefreshTokenInvalidGrant");
    }

    [Given(@"the Google Calendar API will return a 403 for the user")]
    public async Task GivenCalendarApi403()
    {
        await _simulatorApi.SetSyncFailureModeAsync(GetUserId(), "CalendarApi403");
    }

    [When(@"I trigger a manual sync from the Settings page")]
    public async Task WhenITriggerManualSync()
    {
        await TriggerManualSyncAsync();
    }

    [Then(@"I see the reauth banner on the dashboard with a reconnect call to action")]
    public async Task ThenISeeReauthBannerWithCta()
    {
        var page = _scenarioContext.Get<IPage>();
        // FHQ-28: wait for network-idle so Blazor WASM bootstrap + SignalR connect both complete before the locator wait begins.
        await page.GotoAsync(_config.BaseUrl + "/",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await _dashboardPage.ReauthBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        (await _dashboardPage.ReauthBannerCta.IsVisibleAsync()).Should().BeTrue(
            "the reauth banner must render a reconnect CTA when AuthStatus is NeedsReauth");
    }

    [Then(@"I see the reauth banner on the dashboard")]
    public async Task ThenISeeReauthBanner()
    {
        var page = _scenarioContext.Get<IPage>();
        // FHQ-28: wait for network-idle so Blazor WASM bootstrap + SignalR connect both complete before the locator wait begins.
        await page.GotoAsync(_config.BaseUrl + "/",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        try
        {
            await _dashboardPage.ReauthBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
        catch (TimeoutException ex)
        {
            // FHQ-28 diagnostic: surface the WebApi's view of the world directly
            // in the failing test's error message so triage doesn't need WebApi
            // container logs.
            var diag = await CollectReauthDiagnosticAsync(page);
            throw new TimeoutException($"{ex.Message} {diag}", ex);
        }
    }

    [Then(@"the banner shows the reason ""([^""]*)""")]
    public async Task ThenBannerShowsReason(string expected)
    {
        var bannerText = await _dashboardPage.ReauthBanner.InnerTextAsync();
        bannerText.Should().Contain(expected,
            $"the reauth banner must surface the Google-supplied reason '{expected}'");
    }

    [Then(@"the connection status badge reads ""([^""]*)""")]
    public async Task ThenConnectionStatusBadgeReads(string expected)
    {
        await _diagnosticsPage.GotoAsync();
        var label = await _diagnosticsPage.StatusBadge.InnerTextAsync();

        string because;
        if (!label.Contains(expected))
        {
            // FHQ-28 diagnostic: embed the captured sync-response status and the
            // live /api/calendars/connection-status response into the FluentAssertions
            // failure message so triage doesn't need WebApi container logs.
            var page = _scenarioContext.Get<IPage>();
            because = await CollectReauthDiagnosticAsync(page);
        }
        else
        {
            because = $"the diagnostics status badge must read '{expected}' after a reauth-triggering sync";
        }

        label.Should().Contain(expected, because);
    }

    [Then(@"I see a reconnect button on the diagnostics page")]
    public async Task ThenISeeReconnectButton()
    {
        (await _diagnosticsPage.ReconnectBtn.IsVisibleAsync()).Should().BeTrue(
            "the reconnect button must be visible when AuthStatus is NeedsReauth");
    }
}
