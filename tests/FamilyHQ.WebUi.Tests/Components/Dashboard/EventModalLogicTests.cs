using FamilyHQ.Core.DTOs;
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
}
