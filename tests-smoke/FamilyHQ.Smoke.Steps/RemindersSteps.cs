using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;
using Xunit.Abstractions;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RM1 to RM6 — an event's reminders, against the real Google Calendar API.
/// <para>
/// Every expectation in here is derived from what <b>Google returned</b>, never from what the suite or the
/// kiosk sent. Google answers 200 and then rewrites a reminder set silently — clamping an offset,
/// collapsing a duplicate, dropping a method it does not recognise, materialising a calendar's own
/// reminders onto an all-day event — so the value that was sent is not evidence of anything.
/// </para>
/// <para>
/// Overrides are compared as a <b>set</b> throughout, because Google returns them in an order of its own.
/// FluentAssertions' <c>BeEquivalentTo</c> over a collection is order-insensitive, which is why it is used
/// rather than a positional comparison.
/// </para>
/// </summary>
[Binding]
public sealed class RemindersSteps(ScenarioContext scenarioContext, ITestOutputHelper output)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// The reminders a phone puts on an event in these scenarios.
    /// <para>
    /// Two of them, with two different delivery methods, and neither is negotiable. One reminder cannot
    /// show that a comparison is order-independent, and a set FamilyHQ had quietly reduced to the popup
    /// alone would still satisfy a single-reminder check. The email is the member most easily lost: it is a
    /// method the kiosk can create but never picks by default.
    /// </para>
    /// </summary>
    /// <summary>
    /// The description text on the seeded phone-style event. Named rather than inlined because two
    /// scenarios depend on the same string: one writes it, and one asserts it is still there after a
    /// reminder-only edit. Two copies of it would drift, and the assertion would quietly stop checking
    /// anything.
    /// </summary>
    private const string PhoneDescriptionText = "Reminders chosen on a phone, never on the kiosk";

    private static readonly IReadOnlyList<GoogleEventReminderOverride> PhoneReminders =
    [
        new(SmokeReminder.PopupMethod, 45),
        new(SmokeReminder.EmailMethod, 120)
    ];

    /// <summary>
    /// What a family member chooses on the kiosk — stated as an amount and a unit, because that is how the
    /// picker is driven. Deliberately different values from <see cref="PhoneReminders"/>, so a run's output
    /// says which side of the round trip a set came from.
    /// </summary>
    private static readonly IReadOnlyList<SmokeReminder> KioskReminders =
    [
        SmokeReminder.Popup(15, SmokeReminderUnit.Minutes),
        SmokeReminder.Email(3, SmokeReminderUnit.Hours)
    ];

    // ── RM1: the golden rule, applied to reminders ──────────────────────────────

    /// <summary>
    /// Seeds the event <b>in Google</b>, which is the case that matters: reminders are set on a phone far
    /// more often than on the kiosk, and a write path that is correct for FamilyHQ's own events can be
    /// wrong for one it merely synced.
    /// </summary>
    [StepDefinition(@"an event with two reminders exists in Google the way a phone creates one")]
    public async Task AnEventWithTwoRemindersExistsInGoogleTheWayAPhoneCreatesOne()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        var date = state.ReserveFirstDay(SmokeScenarioDays.SingleDay);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Phone-set reminders",
            PhoneDescriptionText,
            date,
            reminderOverrides: PhoneReminders);

        // Google's answer to the insert is the baseline for every later comparison, not the draft: if it
        // rewrote anything on the way in, the rewritten value is what the family will be shown and
        // therefore what a kiosk edit must preserve.
        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Summary;

        output.WriteLine($"seeded in Google with {Describe(state.SeededGoogleEvent.Reminders)}.");
    }

    /// <summary>
    /// Reads the tab back. Two things at once, both new with this feature: that reminders set on a phone
    /// reach the kiosk at all, and that <i>looking</i> at the tab changes nothing — the rename that follows
    /// must still leave Google's copy alone.
    /// </summary>
    [StepDefinition(@"the kiosk shows the reminders Google holds for that event")]
    public async Task TheKioskShowsTheRemindersGoogleHoldsForThatEvent()
    {
        var state = State;
        var expected = AsPairs(SeededOverrides(state));

        var displayed = await state.RequireDashboard().ReadDisplayedRemindersAsync(
            state.ExpectedTitle!, state.EventDate, expected.Count);

        displayed.Select(reminder => reminder.AsGoogleHoldsIt).Should().BeEquivalentTo(
            expected,
            "a reminder set on a phone is only useful on the kiosk if the kiosk shows it. The comparison "
            + "is a set because Google returns an event's overrides reordered, and a missing member here "
            + "means the family cannot see — or change — something they have already chosen");
    }

    [Then(@"Google still holds that event's reminders, as a set")]
    public async Task ThenGoogleStillHoldsThatEventsRemindersAsASet()
    {
        var state = State;
        var original = state.RequireSeededGoogleEvent();
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        // One bounded wait for the rename to land, then everything below comes off that single read — so
        // the title and the reminders cannot be compared against two different moments in time.
        var updated = await BoundedWait.ForAsync(
            async () =>
            {
                var candidate = await state.Environment.Google.GetEventAsync(calendarId, original.Id);
                return candidate.Summary == state.ExpectedTitle ? candidate : null;
            },
            "Google never received the kiosk's title change for this event, so there is nothing to say "
            + "about what that edit did to its reminders. The edit the user asked for has to have happened "
            + "before the thing they did not ask for can be ruled out",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds),
            BoundedWait.GooglePollIntervalMs);

        updated.Reminders.Should().NotBeNull(
            "an event that carried reminders before the edit must still carry a reminders object after it; "
            + "losing it altogether is the widest form of this bug");

        updated.Reminders!.UseDefault.Should().Be(
            original.Reminders!.UseDefault,
            "an event whose reminders were chosen explicitly must stay opted out of its calendar's own. "
            + "Handing it back to the calendar is a different set of alerts at different times, and the "
            + "family would find out by not being told about something");

        AsPairs(updated.Reminders.Overrides ?? []).Should().BeEquivalentTo(
            AsPairs(original.Reminders.Overrides ?? []),
            "a title edit must leave the reminder set exactly as Google held it. This is the golden rule "
            + "in its most expensive form: the family set these on a phone, the kiosk was asked to change "
            + "one unrelated field, and the damage would show up as silence at the moment they expected "
            + "to be alerted");
    }

    // ── RM2 and RM3: what the kiosk writes ──────────────────────────────────────

    [When(@"I create an event on the kiosk with two reminders of its own")]
    [Given(@"the kiosk has created an event with two reminders of its own")]
    public async Task ICreateAnEventOnTheKioskWithTwoRemindersOfItsOwn()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Kiosk-set reminders"),
            Description: state.Correlation.Description("Reminders chosen on the kiosk"),
            CalendarNames: [member],
            Date: state.ReserveFirstDay(SmokeScenarioDays.SingleDay),
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Reminders: KioskReminders);

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = draft.Date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google holds exactly those reminders on that event, as a set")]
    public async Task ThenGoogleHoldsExactlyThoseRemindersOnThatEventAsASet()
    {
        var state = State;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1,
            "Google never settled on exactly one event carrying this scenario's correlation marker for the "
            + "event the kiosk created with reminders of its own");

        var stored = found.Single().Event;
        output.WriteLine($"Google holds {Describe(stored.Reminders)} for the kiosk-created event.");

        stored.Reminders.Should().NotBeNull(
            "reminders chosen on the kiosk have to arrive as reminders; no reminders object at all means "
            + "the write said nothing about them and Google applied its own answer instead");

        stored.Reminders!.UseDefault.Should().NotBe(
            true,
            "an event with reminders of its own does not also follow its calendar's. Google applies the "
            + "overrides when both are present, so this reads as a rewrite that happened to be harmless — "
            + "and the kiosk would show the family a state Google does not hold");

        AsPairs(stored.Reminders.Overrides ?? []).Should().BeEquivalentTo(
            KioskReminders.Select(reminder => reminder.AsGoogleHoldsIt),
            "what arrives in Google is what the Google Calendar app on the family's phones will show and "
            + "act on. An offset that differs means the alert comes at the wrong time; a missing member "
            + "means it never comes. Compared as a set, because Google reorders them");
    }

    [When(@"I hand that event's reminders back to its calendar on the kiosk")]
    public async Task WhenIHandThatEventsRemindersBackToItsCalendarOnTheKiosk()
    {
        var state = State;
        await state.RequireDashboard().UseCalendarDefaultRemindersAsync(state.ExpectedTitle!, state.EventDate);
    }

    [Then(@"Google holds that event as following its calendar's own reminders")]
    public async Task ThenGoogleHoldsThatEventAsFollowingItsCalendarsOwnReminders()
    {
        var state = State;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1 && candidates[0].Event.Reminders?.UseDefault == true,
            "Google never showed this event as following its calendar's own reminders after the kiosk "
            + "handed them back. Following the calendar and carrying an explicit list are two different "
            + "states in Google, and an event stuck in the second one keeps alerting the family at times "
            + "they have already removed");

        var stored = found.Single().Event;
        output.WriteLine($"Google holds {Describe(stored.Reminders)} after the hand-back.");

        (stored.Reminders!.Overrides ?? []).Should().BeEmpty(
            "an event that follows its calendar's own reminders carries none of its own. 'Use the default' "
            + "alongside a list of overrides is a third state, and Google applies the list — so the family "
            + "would still be alerted by the reminders they had just given up");
    }

    // ── RM4: a recording, not an assertion ──────────────────────────────────────

    [When(@"I switch that event to all day on the kiosk")]
    public async Task WhenISwitchThatEventToAllDayOnTheKiosk()
    {
        var state = State;
        await state.RequireDashboard().SwitchToAllDayAsync(state.ExpectedTitle!, state.EventDate);
    }

    /// <summary>
    /// Records what Google does with a revert-to-default reminder body on an all-day event, and asserts
    /// only that it accepted the write.
    /// <para>
    /// <b>Why nothing about the reminders is asserted.</b> Switching an event to all day discards its
    /// reminders, because an all-day reminder is a day and a time while a timed one is an offset from a
    /// start, and Google itself does the same rather than converting them. The kiosk therefore sends
    /// "use the calendar's own" on an all-day event — and no observation of what Google does with that
    /// exists. Every all-day event the spike looked at came back with a calendar's reminders materialised
    /// onto it, never inheriting, and none of them had been sent this body. An expectation written here
    /// would be an invention: either loose enough to pass vacuously, or a guess that blocks every release.
    /// </para>
    /// <para>
    /// What <i>is</i> worth gating on is that Google accepts the write at all, because a rejection breaks
    /// the All-day toggle for the family. So the write is asserted and the outcome is printed. When the
    /// answer has been read off a run and agreed as the intended behaviour, the assertion belongs here —
    /// and if Google rejects the body, that is a product decision about what the kiosk should send
    /// instead, not something to work around in this file.
    /// </para>
    /// </summary>
    [Then(@"Google accepts the write, and what it now holds for those reminders is recorded")]
    public async Task ThenGoogleAcceptsTheWriteAndWhatItNowHoldsForThoseRemindersIsRecorded()
    {
        var state = State;
        var original = state.RequireSeededGoogleEvent();
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        var updated = await BoundedWait.ForAsync(
            async () =>
            {
                var candidate = await state.Environment.Google.GetEventAsync(calendarId, original.Id);
                return candidate.Start?.Date is not null ? candidate : null;
            },
            "Google never showed this event as all-day after the kiosk switched it. That save also asks "
            + "Google to hand the event's reminders back to its calendar, on an event that has just become "
            + "all-day, and what Google does with that has never been observed — a rejection is one of the "
            + "possibilities this scenario exists to find out about. Read what Google holds for the event "
            + "(the correlation id above locates it) and bring the answer back as a product decision: what "
            + "should the kiosk send instead? Do not relax this into a retry",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds),
            BoundedWait.GooglePollIntervalMs);

        output.WriteLine(
            "RECORDED — a revert-to-default reminder body sent to an ALL-DAY event: Google accepted it and "
            + $"now holds {Describe(updated.Reminders)}. Nothing is asserted about that value; see the "
            + "preprod smoke maintenance guide.");
    }

    // ── RM5: the divergence an all-day event creates ────────────────────────────

    [Then(@"Google holds that all-day event as not inheriting, and what it applied is recorded")]
    public async Task ThenGoogleHoldsThatAllDayEventAsNotInheritingAndWhatItAppliedIsRecorded()
    {
        var state = State;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1,
            "Google never settled on exactly one event carrying this scenario's correlation marker for the "
            + "all-day event the kiosk created");

        var stored = found.Single().Event;

        // Printed before the assertion, so the observation survives a failure — the value is the whole
        // point of the scenario and a red run is when somebody reads it.
        output.WriteLine(
            "RECORDED — an all-day event created on the kiosk, which says nothing about reminders: Google "
            + $"applied {Describe(stored.Reminders)}.");

        stored.Reminders.Should().NotBeNull(
            "Google answers with a reminders object for every event, so its absence is a change in the "
            + "API's shape rather than a statement about this event");

        // The one thing that IS asserted, and nothing more. Note what is deliberately absent: no claim
        // that the overrides are non-empty. A reminder that fires after an event has started is invisible
        // to the API, an all-day event starts at midnight, and Google's separate all-day default is not
        // exposed at all — so an empty list here means "none, OR reminders the API will not show you".
        stored.Reminders!.UseDefault.Should().NotBe(
            true,
            "an all-day event does not inherit its calendar's reminders — Google materialises them onto "
            + "the event instead. That is why the kiosk has to show what Google returned rather than what "
            + $"it sent. Google currently answers {Describe(stored.Reminders)}. This is a recorded "
            + "observation about Google, not a FamilyHQ rule: if it has changed its mind, re-establish "
            + "what it does now and move this assertion. Do not relax it, and do not read an empty "
            + "override list as 'this event has no reminders'");
    }

    // ── RM6: the golden rule in the other direction ─────────────────────────

    /// <summary>
    /// The mirror of RM1, and the direction that gets missed. RM1 proves an unrelated edit leaves the
    /// reminders alone; this proves a reminder edit leaves everything else alone.
    /// <para>
    /// Editing reminders is an operation the kiosk did not have at all before this feature, and it goes
    /// out as a whole event resource. Anything that resource omits, Google clears — so the blast radius
    /// of a reminder change is every other field on an event the family created on a phone, and the
    /// damage shows up in the Google Calendar app rather than here.
    /// </para>
    /// <para>
    /// <c>colorId</c> is the sharpest probe available: FamilyHQ neither reads nor writes it, so a colour
    /// that came back cleared could only mean the kiosk sent an event resource that did not carry it.
    /// </para>
    /// <para>
    /// The start's <c>timeZone</c> is <b>recorded rather than asserted</b>, and the distinction is the
    /// point. Nobody has observed what Google returns for a single timed event's zone after a FamilyHQ
    /// reminder write, and this suite gates promotion — so an assertion written from no observation
    /// either passes vacuously or blocks a release on a guess. The same reasoning RM4 sets out. The
    /// value goes into the run's output; once a green run establishes it, the assertion belongs here.
    /// </para>
    /// </summary>
    [Then(@"Google still holds everything else about that event exactly as it was")]
    public async Task ThenGoogleStillHoldsEverythingElseAboutThatEventExactlyAsItWas()
    {
        var state = State;
        var original = state.RequireSeededGoogleEvent();
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        // The wait keys on the change that WAS asked for, and everything below comes off that single
        // read. Two consequences, both load-bearing: no two fields are compared against different
        // moments in time, and the scenario cannot pass vacuously by reading the event back before the
        // kiosk's write ever reached Google.
        var updated = await BoundedWait.ForAsync(
            async () =>
            {
                var candidate = await state.Environment.Google.GetEventAsync(calendarId, original.Id);
                return candidate.Reminders?.UseDefault == true ? candidate : null;
            },
            "Google never showed this event as following its calendar's own reminders after the kiosk "
            + "handed them back, so there is nothing to say about what that edit did to the rest of the "
            + "event. The change the family asked for has to have happened before the changes they did "
            + "not ask for can be ruled out",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds),
            BoundedWait.GooglePollIntervalMs);

        output.WriteLine($"after the reminder-only edit Google holds {Describe(updated.Reminders)}.");

        updated.Summary.Should().Be(
            original.Summary,
            "the title was not edited. A reminder change that also renames the event is the same class of "
            + "damage as a rename that clears the reminders — just the one nobody thinks to look for");

        // The free-text description, not the whole string: FamilyHQ appends its managed [members: …] tag
        // on every write, which is intended and reads as a tag. What must never happen is the family's
        // own words being replaced.
        updated.Description.Should().Contain(
            PhoneDescriptionText,
            "the family's own description text must survive an edit that never touched the description");
        updated.Description.Should().Contain(
            state.Correlation.DescriptionMarker,
            "the rest of the description must survive too, not just its first line");

        updated.Location.Should().Be(
            original.Location, "the location was not edited, so it must come back exactly as it was");

        updated.ColorId.Should().Be(
            original.ColorId, "FamilyHQ has no opinion about an event's colour and must not clear it");

        updated.Start!.DateTime.Should().Be(
            original.Start!.DateTime,
            "the start was not edited. A reminder change that moves the event is the worst outcome in "
            + "this scenario: the family would be alerted correctly, for the wrong time");
        updated.End!.DateTime.Should().Be(original.End!.DateTime, "the end was not edited");

        // Recorded, not asserted — see the remarks. Printed whatever it says, so a run where the zone
        // did change is still legible to whoever reads the output.
        output.WriteLine(
            "RECORDED — the start's timeZone across a reminder-only edit: Google held "
            + $"'{original.Start.TimeZone ?? "none"}' before and '{updated.Start.TimeZone ?? "none"}' "
            + "after. Nothing is asserted about this yet; see the preprod smoke maintenance guide.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static IReadOnlyList<GoogleEventReminderOverride> SeededOverrides(SmokeScenarioState state)
    {
        var seeded = state.RequireSeededGoogleEvent();

        return seeded.Reminders?.Overrides
               ?? throw new InvalidOperationException(
                   "Google returned no reminder overrides for the event this scenario seeded, although it "
                   + "was created with two. There is nothing to compare against, so the scenario stops "
                   + $"here rather than passing vacuously. Google answered {Describe(seeded.Reminders)}.");
    }

    /// <summary>
    /// The <c>(method, minutes)</c> pairs a set comparison is made on. Whole records would make "2 hours"
    /// and "120 minutes" different reminders, which Google does not agree with — it stores only minutes.
    /// </summary>
    private static IReadOnlyList<(string Method, int Minutes)> AsPairs(
        IEnumerable<GoogleEventReminderOverride> overrides) =>
    [
        .. overrides.Select(candidate => (
            candidate.Method ?? throw new InvalidOperationException(
                "Google returned a reminder override with no method, so it cannot be compared by value."),
            candidate.Minutes ?? throw new InvalidOperationException(
                "Google returned a reminder override with no offset, so it cannot be compared by value."))
        )
    ];

    /// <summary>
    /// What Google holds, in one line fit for a test log. Carries no secret and nothing that identifies a
    /// person — a method and an offset are all a reminder is.
    /// </summary>
    private static string Describe(GoogleEventReminders? reminders)
    {
        if (reminders is null)
        {
            return "no reminders object at all";
        }

        var useDefault = reminders.UseDefault switch
        {
            true => "true",
            false => "false",
            null => "absent"
        };

        var overrides = string.Join(
            ", ",
            (reminders.Overrides ?? []).Select(candidate => $"{candidate.Method}:{candidate.Minutes}"));

        return $"useDefault={useDefault}, overrides=[{overrides}]";
    }
}
