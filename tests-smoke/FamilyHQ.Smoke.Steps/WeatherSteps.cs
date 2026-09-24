using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// E1 — the Open-Meteo scenario, and the only one in the suite whose third party is not Google.
/// </summary>
[Binding]
public sealed class WeatherSteps(ScenarioContext scenarioContext)
{
    private const int WeatherStripTimeoutMs = 60000;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// The strip is rendered only when preprod actually has a current reading, and preprod only has one
    /// because it asked Open-Meteo for the coordinates it geocoded from its saved place name — which
    /// preflight has already confirmed is the expected one. So a populated strip is the whole chain
    /// working: saved location, Open-Meteo, the ingest, the API and the kiosk. There is nothing in the
    /// strip's markup that names a place, and there should not be: it is a wall display in someone's
    /// kitchen.
    /// </summary>
    [Then(@"the weather widget shows current conditions")]
    public async Task ThenTheWeatherWidgetShowsCurrentConditions()
    {
        var dashboard = State.RequireDashboard();

        await dashboard.WaitForWeatherStripAsync(WeatherStripTimeoutMs);
        await Assertions.Expect(dashboard.WeatherCurrentConditions).ToBeVisibleAsync();

        var temperature = (await dashboard.WeatherTemperature.InnerTextAsync()).Trim();
        var condition = (await dashboard.WeatherCondition.InnerTextAsync()).Trim();

        temperature.Should().NotBeNullOrWhiteSpace(
            "a rendered strip with an empty temperature would mean the widget appeared without a reading");
        condition.Should().NotBeNullOrWhiteSpace(
            "Open-Meteo's weather code is what produces the condition text; blank means it was not mapped");
    }

    /// <summary>
    /// The app booting cleanly is part of what E1 proves: a Blazor WASM kiosk that throws on start still
    /// paints a dashboard, so "it looks fine" is not evidence. Only genuine errors are counted — console
    /// warnings are noise a real browser emits for reasons outside FamilyHQ's control.
    /// </summary>
    [Then(@"the kiosk reported no console errors")]
    public void ThenTheKioskReportedNoConsoleErrors()
    {
        var problems = State.Driver?.PageProblems ?? [];

        problems.Should().BeEmpty(
            "a console error on boot means something in the kiosk threw, whatever the page looks like");
    }
}
