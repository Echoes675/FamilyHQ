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

    private async Task DumpConnectionStatusDiagnosticAsync(IPage page)
    {
        try
        {
            // Use the page's own fetch so the auth cookie travels with the request.
            var json = await page.EvaluateAsync<string>(@"
                async () => {
                    try {
                        const r = await fetch('/api/calendars/connection-status', { credentials: 'include' });
                        const body = await r.text();
                        return JSON.stringify({ status: r.status, body });
                    } catch (e) {
                        return JSON.stringify({ error: String(e) });
                    }
                }");

            // Also capture page state so we can tell whether the test is on the dashboard
            // and authenticated, vs landed on the login screen.
            var pageState = await page.EvaluateAsync<string>(@"
                () => JSON.stringify({
                    url: window.location.href,
                    title: document.title,
                    hasReauthBanner: !!document.querySelector('[data-testid=""reauth-banner-dashboard""]'),
                    hasLoginButton: !!document.querySelector('button.btn-primary.btn-lg'),
                    bodyTextHead: document.body.innerText.substring(0, 500)
                })");

            // Use Console.Error so it shows up in xUnit Standard Output Messages.
            Console.Error.WriteLine($"[FHQ-28 diagnostic] connection-status response: {json}");
            Console.Error.WriteLine($"[FHQ-28 diagnostic] page state: {pageState}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FHQ-28 diagnostic] failed to collect diagnostics: {ex.Message}");
        }
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
        catch (TimeoutException)
        {
            // FHQ-28 diagnostic: when the banner doesn't appear, capture the
            // current connection-status from the backend so we can discriminate
            // "WebApi didn't mark NeedsReauth" from "UI didn't surface the mark".
            DumpSyncStatusDiagnostic();
            await DumpConnectionStatusDiagnosticAsync(page);
            throw;
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

        if (!label.Contains(expected))
        {
            // FHQ-28 diagnostic: capture sync-response status and connection-status
            // so we can distinguish a silent-200 sync (WebApi never reached the catch)
            // from a 409 sync followed by an unpersisted mark.
            var page = _scenarioContext.Get<IPage>();
            DumpSyncStatusDiagnostic();
            await DumpConnectionStatusDiagnosticAsync(page);
        }

        label.Should().Contain(expected,
            $"the diagnostics status badge must read '{expected}' after a reauth-triggering sync");
    }

    private void DumpSyncStatusDiagnostic()
    {
        if (_scenarioContext.TryGetValue("LastSyncResponseStatus", out int status))
        {
            Console.Error.WriteLine($"[FHQ-28 diagnostic] last /api/sync/trigger response status: {status}");
        }
        else
        {
            Console.Error.WriteLine("[FHQ-28 diagnostic] no LastSyncResponseStatus captured (step didn't run or scenario context cleared).");
        }
    }

    [Then(@"I see a reconnect button on the diagnostics page")]
    public async Task ThenISeeReconnectButton()
    {
        (await _diagnosticsPage.ReconnectBtn.IsVisibleAsync()).Should().BeTrue(
            "the reconnect button must be visible when AuthStatus is NeedsReauth");
    }
}
