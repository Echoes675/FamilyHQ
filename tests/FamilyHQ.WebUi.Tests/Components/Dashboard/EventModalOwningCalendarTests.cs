using FamilyHQ.Core.Models;
using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// EventModalLogic.OwningCalendarDefaults closes a duplication: the modal used to re-implement the
/// server's member-routing rule client-side to guess which calendar an event would land on. An
/// EXISTING event already has a server-stored answer and must read it rather than predict; only a
/// NEW event — which has no owner yet because its member chips are still being chosen — still needs
/// the prediction.
/// </summary>
public class EventModalOwningCalendarTests
{
    private static readonly Guid MemberAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedCalendarId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void OwningCalendarDefaults_ForAnExistingEvent_ReadsTheStoredOwnerRatherThanPredictingIt()
    {
        // The server decides which calendar an event lives on. Predicting it client-side worked only
        // because exactly one calendar is shared; reading what the server stored cannot drift at all.
        //
        // The calendar list here is deliberately NON-empty and would yield a real, different
        // prediction (the shared calendar's own defaults) if the coalesce order inside
        // OwningCalendarDefaults were ever flipped from "stored ?? predicted" to "predicted ?? stored".
        // An empty calendar list can only ever prove "a non-null stored value beats a null prediction",
        // which a flipped `??` would also satisfy — it would not catch the regression this test name
        // claims to catch.
        var stored = EventReminders.Explicit([new EventReminder("popup", 45)]);
        var wrongPrediction = EventReminders.Explicit([new EventReminder("email", 5)]);
        var shared = new CalendarSummaryViewModel(
            SharedCalendarId, "Shared", "#00ff00", IsShared: true, DefaultReminders: wrongPrediction);

        var result = EventModalLogic.OwningCalendarDefaults(
            storedOwningCalendarDefaults: stored,
            selectedCalendarIds: [MemberAId, MemberBId],   // > 1 selected — would predict the shared calendar
            calendars: [shared]);

        result.Should().BeSameAs(stored);
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
            storedOwningCalendarDefaults: null,
            selectedCalendarIds: [],
            calendars: []);

        result.Should().BeNull();
    }
}
