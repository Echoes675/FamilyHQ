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

    [Given(@"Google rejects refresh tokens with ""invalid_grant""")]
    public async Task GivenGoogleRejectsRefreshTokens()
    {
        var userId = GetUserId();
        await _simulatorApi.SetSyncFailureModeAsync(userId, "RefreshTokenInvalidGrant");
    }

    [Given(@"the Google Calendar API rejects requests with ""403""")]
    public async Task GivenTheGoogleCalendarApiRejectsRequestsWith403()
    {
        var userId = GetUserId();
        await _simulatorApi.SetSyncFailureModeAsync(userId, "CalendarApi403");
    }

    [Given(@"a sync event failure has been recorded")]
    public async Task GivenASyncEventFailureHasBeenRecorded()
    {
        var userId = GetUserId();
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        await _simulatorApi.AddPoisonEventAsync(userId, calendarId);
        await TriggerManualSyncAsync();
    }

    [Given(@"I trigger a manual sync")]
    [When(@"I trigger a manual sync")]
    public Task WhenITriggerAManualSync() => TriggerManualSyncAsync();

    [When(@"I view the diagnostics page")]
    public Task WhenIViewTheDiagnosticsPage() => _diagnosticsPage.GotoAsync();

    [Then(@"I see the reauth banner on the dashboard")]
    public async Task ThenISeeTheReauthBannerOnTheDashboard()
    {
        var page = _scenarioContext.Get<IPage>();
        await page.GotoAsync(_config.BaseUrl + "/");
        await _dashboardPage.ReauthBanner.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        (await _dashboardPage.IsReauthBannerVisibleAsync()).Should().BeTrue(
            "the reauth banner must appear on the dashboard once sync has marked the user as needs_reauth");
    }

    [Then(@"the reauth banner shows a reconnect link")]
    public async Task ThenTheReauthBannerShowsAReconnectLink()
    {
        await _dashboardPage.ReauthBannerCta.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var ctaText = (await _dashboardPage.ReauthBannerCta.InnerTextAsync()).Trim();
        ctaText.Should().NotBeNullOrWhiteSpace("the reauth banner CTA must expose a visible action label");
    }

    [Then(@"the reauth banner shows the Google-supplied reason")]
    public async Task ThenTheReauthBannerShowsTheGoogleSuppliedReason()
    {
        var bannerText = await _dashboardPage.GetReauthBannerTextAsync();

        // The Calendar API 403 the simulator injects carries the message
        // "Insufficient Permission". The WebApi forwards the upstream body
        // verbatim into UserTokens.LastError, which the banner formats into
        // its message line.
        bannerText.Should().Contain("Insufficient Permission",
            "the banner must surface the Google-supplied reason, not the default placeholder");
    }

    [Then(@"I see the needs-reauth status badge")]
    public async Task ThenISeeTheNeedsReauthStatusBadge()
    {
        var label = await _diagnosticsPage.GetStatusBadgeTextAsync();
        label.Should().Contain("Needs Reauth",
            "the diagnostics status badge must reflect the needs_reauth connection state");
    }

    [Then(@"I see a reconnect button")]
    public async Task ThenISeeAReconnectButton()
    {
        await _diagnosticsPage.ReconnectBtn.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        (await _diagnosticsPage.ReconnectBtn.IsVisibleAsync()).Should().BeTrue();
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
        await _settingsPage.ClickSyncNowAsync();
    }
}
