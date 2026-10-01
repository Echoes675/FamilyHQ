using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-32: the create-event modal must not silently default the calendar selection.
// The initial selection depends ONLY on an explicitly-passed calendarId — never on
// the order or composition of the user's calendar list. Taking no Calendars argument
// is deliberate: it proves by construction that list order / IsShared distribution
// cannot influence the default (the old bug pre-selected Calendars.FirstOrDefault()).
public class EventModalLogicTests
{
    [Fact]
    public void InitialCreateSelection_WithExplicitCalendarId_SelectsOnlyThatId()
    {
        var calendarId = Guid.NewGuid();

        var result = EventModalLogic.InitialCreateSelection(calendarId);

        result.Should().ContainSingle().Which.Should().Be(calendarId);
    }

    [Fact]
    public void InitialCreateSelection_WithNoCalendarId_IsEmpty()
    {
        var result = EventModalLogic.InitialCreateSelection(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void InitialCreateSelection_WithEmptyGuid_IsEmpty()
    {
        // Agenda view guards against Guid.Empty before calling, but treat it defensively:
        // an empty id is not a real calendar and must not become a selection.
        var result = EventModalLogic.InitialCreateSelection(Guid.Empty);

        result.Should().BeEmpty();
    }

    // ── DecideSave: the Save-button dispatch matrix (FHQ-18.9) ─────────────────

    [Fact]
    public void DecideSave_NewEvent_WithRule_CreatesSeries()
    {
        // Creating a brand-new event with a recurrence rule → native series creation.
        var action = EventModalLogic.DecideSave(isNewEvent: true, wasRecurring: false, hasRuleNow: true);

        action.Should().Be(EventSaveAction.CreateSeries);
    }

    [Fact]
    public void DecideSave_NewEvent_WithoutRule_CreatesSingle()
    {
        var action = EventModalLogic.DecideSave(isNewEvent: true, wasRecurring: false, hasRuleNow: false);

        action.Should().Be(EventSaveAction.Create);
    }

    [Fact]
    public void DecideSave_EditNonRecurring_TurnedRecurrenceOn_UpdatesRecurrenceOn()
    {
        // Editing a previously non-recurring event and switching the picker ON promotes
        // it to a series in place via the single-event update channel (no scope prompt —
        // there is no pre-existing series to scope against).
        var action = EventModalLogic.DecideSave(isNewEvent: false, wasRecurring: false, hasRuleNow: true);

        action.Should().Be(EventSaveAction.UpdateRecurrenceOn);
    }

    [Fact]
    public void DecideSave_EditNonRecurring_StillNonRecurring_UpdatesSingle()
    {
        var action = EventModalLogic.DecideSave(isNewEvent: false, wasRecurring: false, hasRuleNow: false);

        action.Should().Be(EventSaveAction.Update);
    }

    [Fact]
    public void DecideSave_EditRecurring_RuleStillSet_PromptsForScope()
    {
        // Any save of an already-recurring event must first ask "which occurrences?".
        var action = EventModalLogic.DecideSave(isNewEvent: false, wasRecurring: true, hasRuleNow: true);

        action.Should().Be(EventSaveAction.PromptScope);
    }

    [Fact]
    public void DecideSave_EditRecurring_RuleCleared_PromptsForScope()
    {
        // Turning recurrence OFF on a recurring event still routes through the prompt path
        // (which then collapses the whole series); the immediate decision is to prompt.
        var action = EventModalLogic.DecideSave(isNewEvent: false, wasRecurring: true, hasRuleNow: false);

        action.Should().Be(EventSaveAction.PromptScope);
    }

    [Fact]
    public void DecideSave_NewEventThatWasRecurring_IsImpossible_Throws()
    {
        // A brand-new event cannot already be a recurring series — fail fast on the
        // contradictory combination rather than silently picking a branch.
        var act = () => EventModalLogic.DecideSave(isNewEvent: true, wasRecurring: true, hasRuleNow: true);

        act.Should().Throw<ArgumentException>();
    }

    // ── DecideRecurringSave: scope-prompt confirm → service dispatch ───────────

    [Fact]
    public void DecideRecurringSave_NotClearing_DispatchesUpdateRecurring()
    {
        var action = EventModalLogic.DecideRecurringSave(isClearingRecurrence: false);

        action.Should().Be(RecurringSaveAction.UpdateRecurring);
    }

    [Fact]
    public void DecideRecurringSave_Clearing_CollapsesWholeSeries()
    {
        // Turning recurrence OFF is inherently a series-level operation: the chosen pill
        // is ignored and the series is collapsed via ClearRecurrence on the single-event
        // channel, regardless of the scope the user picked in the prompt.
        var action = EventModalLogic.DecideRecurringSave(isClearingRecurrence: true);

        action.Should().Be(RecurringSaveAction.ClearRecurrence);
    }

    // ── EffectiveScope: a cleared series is always whole-series ────────────────

    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    [InlineData(RecurrenceScope.AllInSeries)]
    public void EffectiveScope_WhenClearing_IsAllInSeries(RecurrenceScope chosen)
    {
        EventModalLogic.EffectiveScope(chosen, isClearingRecurrence: true)
            .Should().Be(RecurrenceScope.AllInSeries);
    }

    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    [InlineData(RecurrenceScope.AllInSeries)]
    public void EffectiveScope_WhenNotClearing_IsChosenScope(RecurrenceScope chosen)
    {
        EventModalLogic.EffectiveScope(chosen, isClearingRecurrence: false)
            .Should().Be(chosen);
    }

    // ── DecideDelete: delete dispatch matrix ──────────────────────────────────

    [Fact]
    public void DecideDelete_NonRecurring_DeletesImmediately()
    {
        EventModalLogic.DecideDelete(wasRecurring: false).Should().Be(EventDeleteAction.Delete);
    }

    [Fact]
    public void DecideDelete_Recurring_PromptsForScope()
    {
        EventModalLogic.DecideDelete(wasRecurring: true).Should().Be(EventDeleteAction.PromptScope);
    }

    // ── MembersChanged: order-insensitive set comparison ──────────────────────

    [Fact]
    public void MembersChanged_SameMembersDifferentOrder_IsFalse()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        EventModalLogic.MembersChanged([a, b], [b, a]).Should().BeFalse();
    }

    [Fact]
    public void MembersChanged_DuplicatesCollapse_IsFalse()
    {
        var a = Guid.NewGuid();

        EventModalLogic.MembersChanged([a], [a, a]).Should().BeFalse();
    }

    [Fact]
    public void MembersChanged_MemberAdded_IsTrue()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        EventModalLogic.MembersChanged([a], [a, b]).Should().BeTrue();
    }

    [Fact]
    public void MembersChanged_MemberRemoved_IsTrue()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        EventModalLogic.MembersChanged([a, b], [a]).Should().BeTrue();
    }

    // --- What a save says about reminders -------------------------------------------------------
    //
    // The gate that keeps the Google Calendar app's reminders intact. One outbound body is shared by
    // create, create-recurring and patch, and Google replaces the whole reminders object whenever the
    // key is present — so a save that carries reminders nobody asked to change rewrites what somebody
    // set on a phone. Null is the safe answer and has to be the answer for every untouched tab.

    private static readonly EventReminder PopupTenMinutes = new(EventRemindersValidator.PopupMethod, 10);
    private static readonly EventReminder EmailADayBefore = new(EventRemindersValidator.EmailMethod, 1440);

    private static EventReminders CalendarDefault() => EventReminders.Explicit([PopupTenMinutes]);

    private static ReminderPickerModel Picker(EventReminders? opened, bool isAllDay = false) =>
        ReminderPickerModel.From(opened, CalendarDefault(), isAllDay);

    [Fact]
    public void RemindersToWrite_WhenTheEventFollowsTheCalendarAndNothingWasTouched_IsNull()
    {
        EventModalLogic.RemindersToWrite(Picker(EventReminders.InheritsCalendarDefault)).Should().BeNull();
    }

    [Fact]
    public void RemindersToWrite_WhenTheEventHasItsOwnRemindersAndNothingWasTouched_IsNull()
    {
        // The ordinary edit: a title changes, the Reminders tab is never opened. This is the case the
        // whole feature is built around — the reminders belong to Google and must not be re-sent.
        var picker = Picker(EventReminders.Explicit([PopupTenMinutes, EmailADayBefore]));

        EventModalLogic.RemindersToWrite(picker).Should().BeNull();
    }

    [Fact]
    public void RemindersToWrite_WhenTheEventHasNoRemindersAndNothingWasTouched_IsNull()
    {
        EventModalLogic.RemindersToWrite(Picker(EventReminders.ExplicitlyNone)).Should().BeNull();
    }

    [Fact]
    public void RemindersToWrite_WhenTheEventsRemindersWereNeverSynced_IsNull()
    {
        // Nothing is known about this event's reminders yet, which is not the same as knowing it has
        // none. Writing the tab's guess would replace whatever Google is actually holding.
        EventModalLogic.RemindersToWrite(Picker(null)).Should().BeNull();
    }

    [Fact]
    public void RemindersToWrite_WhenAReminderWasAdded_CarriesTheCompleteSet()
    {
        var picker = Picker(EventReminders.Explicit([PopupTenMinutes]));
        picker.TryAdd(EmailADayBefore).Should().BeTrue();

        var written = EventModalLogic.RemindersToWrite(picker);

        written.Should().NotBeNull();
        // Complete, not a delta: Google replaces the overrides array, so the reminder that was
        // already there has to be re-sent or it is deleted.
        written!.SameAs(EventReminders.Explicit([PopupTenMinutes, EmailADayBefore])).Should().BeTrue();
    }

    [Fact]
    public void RemindersToWrite_WhenTheLastReminderWasRemoved_CarriesTheExplicitlyNoneInstruction()
    {
        var picker = Picker(EventReminders.Explicit([PopupTenMinutes]));
        picker.Remove(PopupTenMinutes).Should().BeTrue();

        var written = EventModalLogic.RemindersToWrite(picker);

        written.Should().NotBeNull();
        written!.UseDefault.Should().BeFalse("asking for no reminders is not asking to follow the calendar");
        written.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void RemindersToWrite_WhenInheritanceWasSwitchedOff_CarriesTheDefaultsAsTheEventsOwn()
    {
        var picker = Picker(EventReminders.InheritsCalendarDefault);
        picker.StopUsingCalendarDefault();

        var written = EventModalLogic.RemindersToWrite(picker);

        // The family chose to stop following the calendar. The values are the same, the instruction is
        // not: the event now keeps these reminders when the calendar's own defaults are changed.
        written.Should().NotBeNull();
        written!.UseDefault.Should().BeFalse();
        written.SameAs(EventReminders.Explicit([PopupTenMinutes])).Should().BeTrue();
    }

    [Fact]
    public void RemindersToWrite_WhenAnEditWasUndone_IsNull()
    {
        var picker = Picker(EventReminders.Explicit([PopupTenMinutes]));
        picker.TryAdd(EmailADayBefore).Should().BeTrue();
        picker.Remove(EmailADayBefore).Should().BeTrue();

        // A visit that ends where it started is not a change, so the save stays silent about
        // reminders rather than re-sending a set that is already Google's.
        EventModalLogic.RemindersToWrite(picker).Should().BeNull();
    }

    [Fact]
    public void RemindersToWrite_AfterTheAllDayToggleResetAnEventsOwnReminders_CarriesRevertToDefault()
    {
        // Switching All day discards the event's reminders and substitutes the all-day shape, as
        // Google does, so the save has to say so — leaving the old timed offsets in place would keep
        // reminders the event can no longer express.
        var picker = Picker(EventReminders.Explicit([PopupTenMinutes]), isAllDay: true);
        picker.UseCalendarDefault();

        var written = EventModalLogic.RemindersToWrite(picker);

        // Only what the kiosk SENDS is pinned here. What Google does with a revert-to-default
        // instruction on an all-day event has not been observed: every all-day event seen so far reads
        // back with the default materialised into explicit overrides rather than inherited, so it may
        // materialise, or be refused. The preprod smoke suite records the real answer against the live
        // API; asserting one here would only pin an assumption.
        written.Should().NotBeNull();
        written!.UseDefault.Should().BeTrue();
        written.Overrides.Should().BeEmpty();
    }
}
