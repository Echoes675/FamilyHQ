using FamilyHQ.Core.Models;
using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// EventModalLogic.OwningCalendarDefaults closes a duplication: the modal used to re-implement the
/// server's member-routing rule client-side to guess which calendar an event would land on. An
/// EXISTING event already has a server-stored answer and must read it rather than predict — and that
/// includes reading a stored-but-null default, which is why the method branches on whether an OWNER
/// is stored (<c>ownerCalendarId.HasValue</c>) and not on whether the DEFAULTS happen to be non-null.
/// Only a NEW event — which has no owner yet because its member chips are still being chosen — still
/// needs the prediction.
/// <para>
/// The prediction's routing itself is no longer stated here: it lives in
/// <c>FamilyHQ.Core.Calendar.OwningCalendarRule</c>, covered by its own tests and pinned to the
/// server's create path by <c>CalendarEventServiceOwningCalendarAgreementTests</c>. What these tests
/// still own is the decision this layer makes — stored owner versus prediction — and the mapping from
/// a calendar to the <c>DefaultReminders</c> the tab reads.
/// </para>
/// </summary>
public class EventModalOwningCalendarTests
{
    private static readonly Guid MemberAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedCalendarId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ExistingEventOwnerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void OwningCalendarDefaults_ForAnExistingEvent_ReadsTheStoredOwnerRatherThanPredictingIt()
    {
        // The server decides which calendar an event lives on. Predicting it client-side worked only
        // because exactly one calendar is shared; reading what the server stored cannot drift at all.
        //
        // The calendar list here is deliberately NON-empty and would yield a real, different
        // prediction (the shared calendar's own defaults) if OwningCalendarDefaults ever went back to
        // branching on whether the DEFAULTS are non-null instead of whether an OWNER is stored. An
        // empty calendar list can only ever prove "a non-null stored value beats a null prediction",
        // which that wrong branch would also satisfy — it would not catch the regression this test
        // name claims to catch.
        var stored = EventReminders.Explicit([new EventReminder("popup", 45)]);
        var wrongPrediction = EventReminders.Explicit([new EventReminder("email", 5)]);
        var shared = new CalendarSummaryViewModel(
            SharedCalendarId, "Shared", "#00ff00", IsShared: true, DefaultReminders: wrongPrediction);

        var result = EventModalLogic.OwningCalendarDefaults(
            ownerCalendarId: ExistingEventOwnerId,
            storedOwningCalendarDefaults: stored,
            selectedCalendarIds: [MemberAId, MemberBId],   // > 1 selected — would predict the shared calendar
            calendars: [shared]);

        result.Should().BeSameAs(stored);
    }

    [Fact]
    public void OwningCalendarDefaults_ForAnExistingEventOnACalendarWithNoDefaultsYet_ReadsNullRatherThanPredicting()
    {
        // The regression this fix exists for: a pre-backfill calendar (DefaultReminders still null)
        // is a real "no defaults" answer for an event that DOES have a stored owner, not a missing
        // one. The old "stored ?? predicted" coalesce could not tell that apart from "nothing stored
        // to read" and fell through to the member-routing prediction below — which, with more than
        // one member selected here, would wrongly produce the shared calendar's own (non-null)
        // defaults instead of null.
        var wrongPrediction = EventReminders.Explicit([new EventReminder("email", 5)]);
        var shared = new CalendarSummaryViewModel(
            SharedCalendarId, "Shared", "#00ff00", IsShared: true, DefaultReminders: wrongPrediction);

        var result = EventModalLogic.OwningCalendarDefaults(
            ownerCalendarId: ExistingEventOwnerId,
            storedOwningCalendarDefaults: null,
            selectedCalendarIds: [MemberAId, MemberBId],
            calendars: [shared]);

        result.Should().BeNull();
    }

    [Fact]
    public void OwningCalendarDefaults_ForANewEvent_StillPredictsFromTheChosenMembers()
    {
        // A new event has no stored owner to read, so the prediction is the only answer available.
        // This is the one case the mirror legitimately serves.
        var sharedDefaults = EventReminders.Explicit([new EventReminder("popup", 10)]);
        var shared = new CalendarSummaryViewModel(
            SharedCalendarId, "Shared", "#00ff00", IsShared: true, DefaultReminders: sharedDefaults);

        var result = EventModalLogic.OwningCalendarDefaults(
            ownerCalendarId: null,
            storedOwningCalendarDefaults: null,
            selectedCalendarIds: [MemberAId, MemberBId],
            calendars: [shared]);

        result.Should().BeSameAs(sharedDefaults);
    }

    [Fact]
    public void OwningCalendarDefaults_ForANewEvent_WithOneMemberSelected_PredictsThatMembersOwnDefaults()
    {
        // Exactly one member selected routes to that member's own calendar, never the shared one —
        // the same single-vs-multi split the server applies when it actually creates the event.
        var memberDefaults = EventReminders.InheritsCalendarDefault;
        var memberCalendar = new CalendarSummaryViewModel(
            MemberAId, "Member A", "#ff0000", DefaultReminders: memberDefaults);
        var shared = new CalendarSummaryViewModel(
            SharedCalendarId, "Shared", "#00ff00", IsShared: true,
            DefaultReminders: EventReminders.ExplicitlyNone);

        var result = EventModalLogic.OwningCalendarDefaults(
            ownerCalendarId: null,
            storedOwningCalendarDefaults: null,
            selectedCalendarIds: [MemberAId],
            calendars: [memberCalendar, shared]);

        result.Should().BeSameAs(memberDefaults);
    }

    [Fact]
    public void OwningCalendarDefaults_ForANewEvent_WithNothingSelectedYet_IsNull()
    {
        // No members chosen yet (the empty-selection state Save blocks on) — nothing to predict from.
        var result = EventModalLogic.OwningCalendarDefaults(
            ownerCalendarId: null,
            storedOwningCalendarDefaults: null,
            selectedCalendarIds: [],
            calendars: []);

        result.Should().BeNull();
    }
}
