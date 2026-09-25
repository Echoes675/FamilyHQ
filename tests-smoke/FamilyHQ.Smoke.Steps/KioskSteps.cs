using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The shared precondition for every scenario that uses the kiosk: the preprod dashboard, open and signed
/// in as the smoke account.
/// </summary>
[Binding]
public sealed class KioskSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// Opens the dashboard. The session was already established by the hook, which wrote the JWT preprod
    /// minted into the kiosk's own token slot — Google's interactive consent screen is not automatable from
    /// CI, which is why those endpoints exist (FHQ-139).
    /// <para>
    /// The signed-in assertion is the dashboard header: it renders only for an authenticated kiosk, so its
    /// presence proves the injected token was accepted rather than merely stored.
    /// </para>
    /// </summary>
    [Given(@"the preprod kiosk is open and signed in")]
    public async Task GivenThePreprodKioskIsOpenAndSignedIn()
    {
        var state = State;
        await state.RequireDashboard().OpenAsync();

        await Assertions.Expect(state.Driver!.Page!.Locator(".dashboard-header")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The UI spot-check. Placement and membership are asserted through preprod's API, which sees the whole
    /// set rather than the three tiles a month cell happens to render — but "the API is right and the screen
    /// is blank" is still a failure the family would notice first, so one scenario per direction looks at the
    /// screen as well.
    /// </summary>
    [Then(@"the kiosk shows that event")]
    public async Task ThenTheKioskShowsThatEvent()
    {
        var state = State;
        await state.RequireDashboard().WaitForEventOnDayAsync(state.Correlation.ShortId, state.EventDate);
    }
}
