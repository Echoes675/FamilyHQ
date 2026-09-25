using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// GK2 and GK4 — the inbound path. An event is made or removed in Google exactly as a phone would do it,
/// and the scenario then waits for the <b>live</b> push to arrive: Google → RelayRobin → preprod's webhook
/// → the sync queue → the API.
/// <para>
/// Nothing here triggers a sync. That is the whole point: a scenario that called
/// <c>POST /api/sync/trigger</c> when the push did not arrive would pass on an environment whose push path
/// is completely dead, which is the exact outage this suite exists to catch (FHQ-141 principle 2).
/// </para>
/// </summary>
[Binding]
public sealed class GoogleToKioskSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// GK2 — created in the shared calendar with two member names in free text, which is how a phone does
    /// it. No <c>[members: …]</c> tag: the tag is authoritative when present, so using one here would test
    /// the easy path and skip the whole-word name matching that real phone-made events rely on.
    /// </summary>
    [StepDefinition(@"an event naming two members is created in Google's shared calendar")]
    public async Task AnEventNamingTwoMembersIsCreatedInGooglesSharedCalendar()
    {
        var state = State;
        var members = state.Environment.Configuration.MemberCalendarNames.Take(2).ToList();
        members.Should().HaveCount(2, "Smoke__MemberCalendars must name at least two member calendars");

        var shared = state.Environment.Configuration.SharedCalendar;
        var calendarId = state.Environment.Calendars.RequireGoogleId(shared);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Shared outing",
            $"Swimming with {members[0]} and {members[1]}",
            SmokeEventShape.Date);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = shared;
        state.MemberNames = members;
        state.EventDate = SmokeEventShape.Date;
        state.ExpectedTitle = draft.Summary;
    }

    /// <summary>GK4's precondition — a phone-made event on a member's own calendar.</summary>
    [StepDefinition(@"an event is created in Google on a member's calendar")]
    public async Task AnEventIsCreatedInGoogleOnAMembersCalendar()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Phone-made appointment",
            "Created directly in Google by the preprod smoke suite",
            SmokeEventShape.Date);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = SmokeEventShape.Date;
        state.ExpectedTitle = draft.Summary;
    }

    /// <summary>
    /// Waits for the live push to arrive. The wait is the assertion: when it expires, the push path did not
    /// deliver, and that is a real failure rather than something to have another go at.
    /// </summary>
    [StepDefinition(@"the kiosk has received that event")]
    public async Task TheKioskHasReceivedThatEvent()
    {
        var state = State;

        await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count > 0,
            "preprod never served the event that was created in Google. The change had to travel Google → "
            + "RelayRobin → preprod's /api/sync/webhook → the sync queue → the API, and one of those links "
            + "did not carry it. Preflight confirmed a live push channel, so start with RelayRobin and "
            + "Sync:WebhookBaseUrl");
    }

    [Then(@"preprod serves that event for both named members and no others")]
    public async Task ThenPreprodServesThatEventForBothNamedMembersAndNoOthers()
    {
        var state = State;

        var found = await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1
                          && candidates[0].Members.Count == state.MemberNames.Count,
            $"preprod never settled on one event with exactly {state.MemberNames.Count} members for this "
            + "scenario. A shared-calendar event whose description names two members must resolve to those "
            + "two and nobody else");

        found.Should().ContainSingle().Which.Members
            .Select(member => member.DisplayName)
            .Should().BeEquivalentTo(
                state.MemberNames,
                "member names are matched as whole words anywhere in the description, so the result must be "
                + "exactly the two named — an extra member means the matcher is too greedy, a missing one "
                + "means it is too strict");
    }

    [When(@"that event is deleted in Google")]
    public async Task WhenThatEventIsDeletedInGoogle()
    {
        var state = State;
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        await state.Environment.Google.DeleteEventAsync(
            calendarId, state.RequireSeededGoogleEvent().Id);
    }

    [Then(@"preprod stops serving that event")]
    public async Task ThenPreprodStopsServingThatEvent()
    {
        var state = State;

        await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 0,
            "preprod is still serving an event that was deleted in Google. An event the family removed on a "
            + "phone but that stays on the kiosk is the visible half of a broken inbound sync");
    }
}
