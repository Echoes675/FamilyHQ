using FamilyHQ.Smoke.Common.Pages;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// KG1, KG1b, KG3 and KG4 — what the kiosk actually writes to Google.
/// <para>
/// Every assertion here reads from Google, never from FamilyHQ. Asking FamilyHQ what it wrote would only
/// establish that FamilyHQ agrees with itself; Google is the system of record and the Google Calendar app
/// on the family's phones is what reads the result.
/// </para>
/// </summary>
[Binding]
public sealed class KioskToGoogleSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// KG1 — a multi-member event. The calendar model says this is written <b>once</b>, to the shared
    /// container, with a members tag; two copies on two member calendars would be the failure.
    /// </summary>
    [StepDefinition(@"I create an event on the kiosk for two members")]
    public async Task ICreateAnEventOnTheKioskForTwoMembers()
    {
        var members = State.Environment.Configuration.MemberCalendarNames.Take(2).ToList();
        members.Should().HaveCount(2, "Smoke__MemberCalendars must name at least two member calendars");

        await CreateOnKioskAsync("Two-member outing", members);
    }

    /// <summary>
    /// KG1b — a single-member event, which belongs on that member's own calendar and nowhere else. The second
    /// phrasing is for KG4, where the same act is a precondition rather than the thing under test: one
    /// implementation, two readings, so the delete scenario cannot drift away from the create it depends on.
    /// </summary>
    [StepDefinition(@"I create an event on the kiosk for one member")]
    [Given(@"the kiosk has created an event for one member")]
    public async Task ICreateAnEventOnTheKioskForOneMember()
    {
        var member = State.Environment.Configuration.MemberCalendarNames.First();
        await CreateOnKioskAsync("Single-member appointment", [member]);
    }

    [Then(@"Google holds exactly one matching event, on the shared calendar")]
    public async Task ThenGoogleHoldsExactlyOneMatchingEventOnTheSharedCalendar()
    {
        var state = State;
        var shared = state.Environment.Configuration.SharedCalendar;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1,
            $"Google never settled on exactly one event carrying this scenario's correlation marker. A "
            + $"two-member event must be written once, to '{shared}'; more than one means it was also "
            + "written to a member calendar, and none means the kiosk write never reached Google");

        found.Should().ContainSingle()
            .Which.CalendarName.Should().Be(
                shared,
                "a multi-member event is placed in the shared container, not on a member calendar");
    }

    [Then(@"Google holds exactly one matching event, on that member's calendar")]
    public async Task ThenGoogleHoldsExactlyOneMatchingEventOnThatMembersCalendar()
    {
        var state = State;
        var member = state.MemberNames.Single();

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1,
            $"Google never settled on exactly one event carrying this scenario's correlation marker for "
            + $"member '{member}'");

        found.Should().ContainSingle()
            .Which.CalendarName.Should().Be(
                member,
                "a single-member event belongs on that member's own calendar");
    }

    [Then(@"the shared calendar holds no matching event")]
    public async Task ThenTheSharedCalendarHoldsNoMatchingEvent()
    {
        var state = State;
        var shared = state.Environment.Configuration.SharedCalendar;

        var found = await SmokeLookup.FindInGoogleAsync(state, state.EventDate);

        found.Where(location => location.CalendarName == shared).Should().BeEmpty(
            "a single-member event must not also appear in the shared container — that is the duplicate "
            + "the family would see twice in the Google Calendar app");
    }

    [Then(@"the event's description names both members")]
    public async Task ThenTheEventsDescriptionNamesBothMembers()
    {
        var state = State;
        var found = await SmokeLookup.FindInGoogleAsync(state, state.EventDate);
        var description = found.Should().ContainSingle().Subject.Event.Description;

        SmokeMemberTag.NamesIn(description).Should().BeEquivalentTo(
            state.MemberNames,
            "the members tag is how the Google side records who a shared-calendar event belongs to");

        description.Should().Contain(
            state.Correlation.DescriptionMarker,
            "FamilyHQ keeps the user's own description text above the managed tag; losing it here would "
            + "mean the tag was written over the description instead of alongside it");
    }

    // ── KG3: the golden rule ────────────────────────────────────────────────────

    /// <summary>
    /// KG3's precondition. The event is created <b>in Google</b>, not on the kiosk, because that is the
    /// majority case: most events are made in the Google Calendar app on a phone, and a change that is
    /// correct for an event FamilyHQ created can be wrong for one it merely synced.
    /// </summary>
    [StepDefinition(@"an event exists in Google the way a phone creates one")]
    public async Task AnEventExistsInGoogleTheWayAPhoneCreatesOne()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Phone-made event",
            "Bring the tickets and the flask",
            SmokeEventShape.Date);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = SmokeEventShape.Date;
        state.ExpectedTitle = draft.Summary;
    }

    /// <summary>
    /// Changes the title and nothing else. The page object touches no other control, so whatever Google
    /// holds afterwards for the other fields is entirely FamilyHQ's choice of what to send back.
    /// </summary>
    [When(@"I change only that event's title on the kiosk")]
    public async Task WhenIChangeOnlyThatEventsTitleOnTheKiosk()
    {
        var state = State;
        var renamed = state.Correlation.Title("Retitled on the kiosk");

        await state.RequireDashboard().RenameEventAsync(state.ExpectedTitle!, renamed, state.EventDate);
        state.ExpectedTitle = renamed;
    }

    [Then(@"Google still holds every other field of that event unchanged")]
    public async Task ThenGoogleStillHoldsEveryOtherFieldOfThatEventUnchanged()
    {
        var state = State;
        var original = state.RequireSeededGoogleEvent();
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        // One bounded wait for the rename to land, then a single read. Everything asserted below comes from
        // that one read of Google, so the fields cannot be compared against different moments in time.
        var updated = await Common.Helpers.BoundedWait.ForAsync(
            async () =>
            {
                var candidate = await state.Environment.Google.GetEventAsync(calendarId, original.Id);
                return candidate.Summary == state.ExpectedTitle ? candidate : null;
            },
            "Google never received the kiosk's title change for this event",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));

        // The free-text description, not the whole string: FamilyHQ appends its managed [members: …] tag on
        // every write, which is intended and visible to the user as a tag rather than as lost text. What must
        // never happen is the user's own words being replaced.
        updated.Description.Should().Contain(
            "Bring the tickets and the flask",
            "the user's own description text must survive an edit that never touched the description");
        updated.Description.Should().Contain(
            state.Correlation.DescriptionMarker,
            "the rest of the description must survive too, not just its first line");

        updated.Location.Should().Be(
            original.Location, "the location was not edited, so it must come back exactly as it was");

        // colorId is the purest probe available: FamilyHQ neither reads nor writes it, so a change here could
        // only come from FamilyHQ sending a whole event resource that omitted it.
        updated.ColorId.Should().Be(
            original.ColorId, "FamilyHQ has no opinion about an event's colour and must not clear it");

        updated.Start!.DateTime.Should().Be(original.Start!.DateTime, "the start was not edited");
        updated.End!.DateTime.Should().Be(original.End!.DateTime, "the end was not edited");

        updated.Reminders!.UseDefault.Should().Be(
            original.Reminders!.UseDefault,
            "an event that opted out of the calendar's default reminders must stay opted out");
        updated.Reminders.Overrides.Should().BeEquivalentTo(
            original.Reminders.Overrides,
            "the reminder the phone set is the one the family will be shown; a title edit must not move it");
    }

    [Then(@"Google holds the new title")]
    public async Task ThenGoogleHoldsTheNewTitle()
    {
        var state = State;
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);
        var updated = await state.Environment.Google.GetEventAsync(
            calendarId, state.RequireSeededGoogleEvent().Id);

        updated.Summary.Should().Be(
            state.ExpectedTitle,
            "the edit the user did ask for must actually have happened — a scenario that only proved "
            + "nothing changed would pass for a write that never went out");
    }

    // ── KG4: delete ─────────────────────────────────────────────────────────────

    [When(@"I delete that event on the kiosk")]
    public async Task WhenIDeleteThatEventOnTheKiosk()
    {
        var state = State;
        await state.RequireDashboard().DeleteEventAsync(state.ExpectedTitle!, state.EventDate);
    }

    [Then(@"Google no longer holds the event")]
    public async Task ThenGoogleNoLongerHoldsTheEvent()
    {
        var state = State;

        await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 0,
            "Google still lists an event carrying this scenario's correlation marker after the kiosk "
            + "deleted it. A delete that succeeds locally but leaves the event in Google is the worst "
            + "shape of this bug: the family sees it come back on the next sync");
    }

    private async Task CreateOnKioskAsync(string baseTitle, IReadOnlyList<string> members)
    {
        var state = State;
        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title(baseTitle),
            Description: state.Correlation.Description("Created by the preprod smoke suite"),
            CalendarNames: members,
            Date: SmokeEventShape.Date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime);

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = members;
        state.EventDate = draft.Date;
        state.ExpectedTitle = draft.Title;
    }
}
