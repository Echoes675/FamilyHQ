using System.Text.RegularExpressions;
using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// FHQ-43: covers the Location-tab time-zone picker UX and the outbound IANA payload. The picker
// resolves the user's effective zone (explicit setting → derived → ip-api → UTC); per the
// determinism rule we never assert an auto-DETECTED value (ip-api returns the env-dependent host
// zone), only the auto/saved BADGE state and EXPLICITLY-selected zone values.
[Binding]
public class TimeZoneSettingsSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly IPage _page;
    private readonly SettingsPage _settingsPage;
    private readonly SimulatorApiClient _simulatorApi;

    public TimeZoneSettingsSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(_page);
        _simulatorApi = simulatorApi;
    }

    // ── Navigation ───────────────────────────────────────────────────────────────

    [Given(@"I open the Location settings tab")]
    public async Task GivenIOpenTheLocationSettingsTab()
    {
        await _settingsPage.NavigateToLocationTabAsync();
        // The time-zone section renders after its settings load; wait for the pill so subsequent
        // selects/asserts don't race the initial paint.
        await Assertions.Expect(_settingsPage.TimeZonePill).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    // ── Time-zone picker actions ──────────────────────────────────────────────────

    [When(@"I select the timezone ""([^""]*)""")]
    public async Task WhenISelectTheTimezone(string ianaZone)
    {
        await _settingsPage.TimeZoneSelect.SelectOptionAsync(new SelectOptionValue { Value = ianaZone });
    }

    [When(@"I save the timezone")]
    public async Task WhenISaveTheTimezone()
    {
        // The select was set by the previous step; the page object re-selects defensively then saves
        // and waits for the PUT to complete so the badge-state assertion doesn't race the re-render.
        var selected = await _settingsPage.TimeZoneSelect.InputValueAsync();
        await _settingsPage.SelectAndSaveTimeZoneAsync(selected);
    }

    // Compound Given used by scenarios that need to START from an explicit, saved zone.
    [Given(@"I have selected and saved the timezone ""([^""]*)""")]
    public async Task GivenIHaveSelectedAndSavedTheTimezone(string ianaZone)
    {
        await _settingsPage.SelectAndSaveTimeZoneAsync(ianaZone);
        await Assertions.Expect(_settingsPage.TimeZoneEffective)
            .ToHaveTextAsync(ianaZone, new() { Timeout = 30000 });
        await Assertions.Expect(_settingsPage.TimeZoneBadge)
            .ToHaveClassAsync(new Regex("badge--saved"), new() { Timeout = 30000 });
    }

    [When(@"I reset the timezone to automatic")]
    public async Task WhenIResetTheTimezoneToAutomatic()
    {
        await _settingsPage.ResetTimeZoneToAutoAsync();
    }

    // ── Location actions used by the independence scenario ─────────────────────────

    [Given(@"I have saved the location ""([^""]*)""")]
    public async Task GivenIHaveSavedTheLocation(string placeName)
    {
        await _settingsPage.PlaceNameInput.FillAsync(placeName);
        var responseTask = _page.WaitForResponseAsync(
            r => r.Url.Contains("api/settings/location") && r.Request.Method == "POST",
            new() { Timeout = 30000 });
        await _settingsPage.SaveLocationBtn.ClickAsync();
        await responseTask;
        // Confirm the location is now explicitly saved before the scenario proceeds.
        await Assertions.Expect(_settingsPage.LocationPillBadge)
            .ToHaveTextAsync("Saved", new() { Timeout = 30000 });
    }

    [When(@"I reset the location to automatic")]
    public async Task WhenIResetTheLocationToAutomatic()
    {
        var responseTask = _page.WaitForResponseAsync(
            r => r.Url.Contains("api/settings/location") && r.Request.Method == "DELETE",
            new() { Timeout = 30000 });
        await _settingsPage.ResetLocationBtn.ClickAsync();
        await responseTask;
    }

    // ── Assertions on picker state ─────────────────────────────────────────────────

    [Then(@"the timezone is shown as auto-detected")]
    public async Task ThenTheTimezoneIsShownAsAutoDetected()
    {
        // Determinism: assert ONLY the Auto badge state — never the resolved zone value, which is
        // the env-dependent ip-api result on a fresh user.
        await Assertions.Expect(_settingsPage.TimeZoneBadge)
            .ToHaveClassAsync(new Regex("badge--auto"), new() { Timeout = 30000 });
    }

    [Then(@"the timezone is shown as ""([^""]*)"" and saved")]
    public async Task ThenTheTimezoneIsShownAsAndSaved(string ianaZone)
    {
        await Assertions.Expect(_settingsPage.TimeZoneEffective)
            .ToHaveTextAsync(ianaZone, new() { Timeout = 30000 });
        await Assertions.Expect(_settingsPage.TimeZoneBadge)
            .ToHaveClassAsync(new Regex("badge--saved"), new() { Timeout = 30000 });
    }

    [Then(@"the timezone is still shown as ""([^""]*)"" and saved")]
    public async Task ThenTheTimezoneIsStillShownAsAndSaved(string ianaZone)
    {
        await ThenTheTimezoneIsShownAsAndSaved(ianaZone);
    }

    // ── Outbound payload assertion (Part B) ────────────────────────────────────────

    [Then(@"the event ""([^""]*)"" was sent to Google with timezone ""([^""]*)""")]
    public async Task ThenTheEventWasSentToGoogleWithTimezone(string summary, string expectedZone)
    {
        var userId = ResolveActiveUserId();
        var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");

        string? actualZone = null;
        await Polling.UntilAsync(
            async () =>
            {
                actualZone = await _simulatorApi.GetEventStartTimeZoneAsync(userId, calendarId, summary);
                return actualZone == expectedZone;
            },
            failMessage:
                $"The Simulator did not record event '{summary}' on calendar '{calendarId}' with " +
                $"start timeZone '{expectedZone}'. Last observed value: '{actualZone ?? "(not found)"}'. " +
                "The app must anchor a recurring timed event to the user's configured IANA zone " +
                "(Google start.timeZone) — FHQ-43.",
            timeoutMs: 30000,
            intervalMs: 500);

        actualZone.Should().Be(expectedZone,
            $"the recurring event '{summary}' must be sent to Google anchored to the configured IANA zone.");
    }

    // The simulator stores events keyed by the scenario's isolated unique username (the WebApi's
    // bearer token resolves to it). UserSteps stashes it as "UniqueUsername:TimedEventsUser"; fall
    // back to the UserTemplate's UserName for robustness.
    private string ResolveActiveUserId()
    {
        if (_scenarioContext.TryGetValue<string>("UniqueUsername:TimedEventsUser", out var byKey)
            && !string.IsNullOrEmpty(byKey))
        {
            return byKey;
        }

        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        return template.UserName;
    }
}
