using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure selection and dispatch logic for the event modal, extracted so it can be
/// unit-tested without rendering the Blazor component (the project has no bUnit;
/// render/interaction is covered by E2E in FHQ-18.11).
/// </summary>
public static class EventModalLogic
{
    /// <summary>
    /// Computes the calendar selection a freshly-opened create modal should start with.
    /// </summary>
    /// <remarks>
    /// FHQ-32: the create modal must NOT silently default to any calendar. Only an
    /// explicitly-passed <paramref name="calendarId"/> (e.g. the Agenda view tapping a
    /// specific column) seeds the selection; otherwise the modal opens with nothing
    /// selected and the existing empty-selection validation blocks Save until the user
    /// picks one. This method deliberately takes no calendar list — the default can never
    /// depend on list order or shared/personal composition.
    /// </remarks>
    public static HashSet<Guid> InitialCreateSelection(Guid? calendarId) =>
        calendarId is { } id && id != Guid.Empty ? [id] : [];

    /// <summary>
    /// Decides what the Save button should do, before any recurrence-scope prompt.
    /// </summary>
    /// <param name="isNewEvent">True when creating; false when editing an existing event.</param>
    /// <param name="wasRecurring">True when the loaded event was already part of a series.</param>
    /// <param name="hasRuleNow">True when the picker currently holds a non-null RRULE.</param>
    /// <remarks>
    /// FHQ-18.9 Save matrix:
    /// <list type="bullet">
    /// <item>New + rule → <see cref="EventSaveAction.CreateSeries"/> (native series).</item>
    /// <item>New + no rule → <see cref="EventSaveAction.Create"/>.</item>
    /// <item>Edit, was non-recurring, rule now set → <see cref="EventSaveAction.UpdateRecurrenceOn"/> (toggle ON).</item>
    /// <item>Edit, was non-recurring, no rule → <see cref="EventSaveAction.Update"/>.</item>
    /// <item>Edit, was recurring → <see cref="EventSaveAction.PromptScope"/> (rule set or cleared).</item>
    /// </list>
    /// </remarks>
    public static EventSaveAction DecideSave(bool isNewEvent, bool wasRecurring, bool hasRuleNow)
    {
        // A brand-new event cannot already be a recurring series — guard the contradiction
        // rather than letting it fall through to an arbitrary branch.
        if (isNewEvent && wasRecurring)
            throw new ArgumentException("A new event cannot already be recurring.", nameof(wasRecurring));

        if (isNewEvent)
            return hasRuleNow ? EventSaveAction.CreateSeries : EventSaveAction.Create;

        if (wasRecurring)
            return EventSaveAction.PromptScope;

        return hasRuleNow ? EventSaveAction.UpdateRecurrenceOn : EventSaveAction.Update;
    }

    /// <summary>
    /// After the user confirms the scope prompt for a save of an already-recurring event,
    /// decides which service channel to use.
    /// </summary>
    /// <remarks>
    /// Turning recurrence OFF (rule now null) collapses the series via the single-event
    /// channel with <c>ClearRecurrence</c> — a whole-series operation. Otherwise the chosen
    /// scope drives <c>UpdateRecurringAsync</c>.
    /// </remarks>
    public static RecurringSaveAction DecideRecurringSave(bool isClearingRecurrence) =>
        isClearingRecurrence ? RecurringSaveAction.ClearRecurrence : RecurringSaveAction.UpdateRecurring;

    /// <summary>
    /// The scope that actually applies to the operation. Clearing recurrence is inherently a
    /// series-level op, so the user's chosen pill is overridden to
    /// <see cref="RecurrenceScope.AllInSeries"/>; otherwise the chosen scope stands.
    /// </summary>
    public static RecurrenceScope EffectiveScope(RecurrenceScope chosen, bool isClearingRecurrence) =>
        isClearingRecurrence ? RecurrenceScope.AllInSeries : chosen;

    /// <summary>
    /// Decides what the Delete button should do: delete immediately for a non-recurring event,
    /// or show the scope prompt for a recurring one.
    /// </summary>
    public static EventDeleteAction DecideDelete(bool wasRecurring) =>
        wasRecurring ? EventDeleteAction.PromptScope : EventDeleteAction.Delete;

    /// <summary>
    /// Whether the edited member (calendar) selection differs from the originally-loaded set.
    /// Order- and duplicate-insensitive (set comparison). Passed to the scope prompt as
    /// <c>MemberChangePending</c> so it can block a member change at a non-All scope
    /// (FHQ-18 §10.1).
    /// </summary>
    public static bool MembersChanged(IEnumerable<Guid> originalMemberIds, IEnumerable<Guid> editedMemberIds) =>
        !originalMemberIds.ToHashSet().SetEquals(editedMemberIds);
}
