using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// GK1, GK2, GK3 and GK4 — the inbound path. An event is made, changed or removed in Google exactly as a
/// phone would do it, and the scenario then waits for the <b>live</b> push to arrive: Google → RelayRobin →
/// preprod's webhook → the sync queue → the API.
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

        var date = state.ReserveFirstDay(SmokeScenarioDays.SingleDay);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Shared outing",
            $"Swimming with {members[0]} and {members[1]}",
            date);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = shared;
        state.MemberNames = members;
        state.EventDate = date;
        state.ExpectedTitle = draft.Summary;
    }

    /// <summary>GK4's precondition — a phone-made event on a member's own calendar.</summary>
    [StepDefinition(@"an event is created in Google on a member's calendar")]
    public async Task AnEventIsCreatedInGoogleOnAMembersCalendar()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        var date = state.ReserveFirstDay(SmokeScenarioDays.SingleDay);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Phone-made appointment",
            "Created directly in Google by the preprod smoke suite",
            date);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = date;
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

    /// <summary>
    /// The other half of inbound membership: an event on a member's own calendar whose description names
    /// nobody. The owning calendar is the only signal there is, so the event belongs to that member and to
    /// nobody else — and a matcher that read the title, the location or the venue name would say otherwise.
    /// </summary>
    [Then(@"preprod serves that event for that member and no others")]
    public async Task ThenPreprodServesThatEventForThatMemberAndNoOthers()
    {
        var state = State;
        var member = state.MemberNames.Single();

        var found = await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1 && candidates[0].Members.Count == 1,
            $"preprod never settled on one event belonging to exactly one member for this scenario. An "
            + $"event on '{member}'s own calendar that names nobody in its description belongs to that "
            + "member alone");

        found.Should().ContainSingle().Which.Members
            .Select(candidate => candidate.DisplayName)
            .Should().BeEquivalentTo(
                new[] { member },
                "the calendar an event sits on is who it belongs to when its description names no one — an "
                + "extra member here means something other than the description is being read as a name");
    }

    // ── GK3: the members an event names, changed on the phone ───────────────────

    /// <summary>
    /// Changes which members the description names, keeping the count at two.
    /// <para>
    /// Two to two on purpose. Going from two members to one, or from one to two, additionally asks FamilyHQ
    /// to move the event between the shared container and a member calendar — a different behaviour, with
    /// its own ticket, and bundling it in here would leave a failure that does not say which of the two
    /// things broke.
    /// </para>
    /// </summary>
    [When(@"the description is changed in Google to name a different pair of members")]
    public async Task WhenTheDescriptionIsChangedInGoogleToNameADifferentPairOfMembers()
    {
        var state = State;
        var members = state.Environment.Configuration.MemberCalendarNames;

        members.Should().HaveCountGreaterThanOrEqualTo(
            3, "Smoke__MemberCalendars must name at least three member calendars for one to be swapped");

        var replacement = new[] { members[1], members[2] };
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        await state.Environment.Google.PatchEventAsync(
            calendarId,
            state.RequireSeededGoogleEvent().Id,
            new GoogleEventPatch(
                Description: state.Correlation.Description(
                    $"Swimming with {replacement[0]} and {replacement[1]}")));

        state.MemberNames = replacement;
    }

    [Then(@"preprod serves that event for the newly named members and no others")]
    public async Task ThenPreprodServesThatEventForTheNewlyNamedMembersAndNoOthers()
    {
        var state = State;
        var expected = state.MemberNames;

        var found = await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1
                          && candidates[0].Members
                              .Select(member => member.DisplayName)
                              .OrderBy(name => name, StringComparer.Ordinal)
                              .SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal)),
            $"preprod never settled on the pair of members the description now names "
            + $"({string.Join(" and ", expected)}). A membership change made on a phone has to reach the "
            + "kiosk: the member who was dropped keeps seeing an event that is no longer theirs, and the "
            + "one who was added never sees it at all");

        found.Should().ContainSingle(
            "renaming the members in a description must not produce a second copy of the event");
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
