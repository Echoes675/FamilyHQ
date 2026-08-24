using FamilyHQ.Core.Calendar;
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

    /// <summary>
    /// Turns a wall-clock value the pickers produced into the instant the model stores.
    /// </summary>
    /// <param name="wallClock">The date (and, for a timed event, time) the user picked.</param>
    /// <param name="isAllDay">Whether the event is all-day.</param>
    /// <param name="viewerOffset">
    /// The viewer's UTC offset at <paramref name="wallClock"/>. Passed in rather than read from
    /// <c>TimeZoneInfo.Local</c> so this stays a pure function; the component supplies it.
    /// </param>
    /// <remarks>
    /// FHQ-174. An all-day boundary is a DATE, not an instant: Google sends <c>"yyyy-MM-dd"</c> and
    /// expects the same string back. Stamping the BROWSER's offset on it — what this did before —
    /// stored 23:00Z on the previous day for any positive offset, because the <c>Start</c>/<c>End</c>
    /// EF converter reduces the value to a UTC instant; the outbound all-day mapping then formats
    /// that instant and sends Google the wrong day. Midnight UTC is the representation that survives
    /// the converter untouched, and it is the same one the sync path now produces for a date Google
    /// sent.
    /// A timed event's wall clock genuinely IS in the viewer's zone, so there the offset is right.
    /// <para>
    /// Routing a boundary through here is what makes it canonical, and this function is reached only
    /// when something re-derives a boundary. Flipping <c>IsAllDay</c> on its own does NOT: the model
    /// keeps whatever instants it already held, which for a create modal is the 09:00 local default.
    /// That is why the toggle calls <see cref="AllDayWallClocks"/>/<see cref="TimedWallClocks"/> and
    /// re-assigns both boundaries rather than only re-routing the getters.
    /// </para>
    /// </remarks>
    public static DateTimeOffset ToModelInstant(DateTime wallClock, bool isAllDay, TimeSpan viewerOffset) =>
        isAllDay
            ? GoogleAllDayDate.AtMidnightUtc(wallClock)
            : new DateTimeOffset(wallClock, viewerOffset);

    /// <summary>
    /// The inverse of <see cref="ToModelInstant"/>: the wall clock the pickers should display for a
    /// stored instant. All-day boundaries are read back in UTC so the date survives a viewer whose
    /// offset is negative; a timed event is shown in the viewer's own zone.
    /// </summary>
    public static DateTime ToPickerWallClock(DateTimeOffset instant, bool isAllDay) =>
        isAllDay ? instant.UtcDateTime : instant.LocalDateTime;

    /// <summary>
    /// The time of day a create modal opens on, and the one an event that has never been timed falls
    /// back to when the all-day toggle is switched off. Same 09:00–10:00 as
    /// <c>EventModal.OpenForCreate</c>, deliberately: a user who turns all-day off should land on the
    /// slot they would have got by creating a timed event on that day, not on 00:00–00:00.
    /// </summary>
    public static readonly TimeSpan DefaultStartTimeOfDay = TimeSpan.FromHours(9);

    /// <inheritdoc cref="DefaultStartTimeOfDay"/>
    public static readonly TimeSpan DefaultEndTimeOfDay = TimeSpan.FromHours(10);

    /// <summary>The span a boundary pair falls back to when the derived end is not after the start.</summary>
    public static readonly TimeSpan FallbackTimedDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// The <c>End</c> to fall back to when moving the start has pushed it past the end.
    /// </summary>
    /// <remarks>
    /// FHQ-174. An all-day event's minimum span is one whole DAY, and its <c>End</c> has to remain a
    /// midnight-UTC boundary. Adding an hour — right for a timed event, and what this did for every
    /// event before — stored a non-midnight all-day <c>End</c> and would have sent Google a
    /// zero-length day, as well as putting a row into the boundary audit that the fixed code created.
    /// </remarks>
    public static DateTimeOffset MinimumEnd(DateTimeOffset start, bool isAllDay) =>
        isAllDay ? start.AddDays(1) : start + FallbackTimedDuration;

    /// <summary>
    /// The wall clocks the all-day toggle must store when it is switched ON, given the two dates the
    /// pickers are currently showing.
    /// </summary>
    /// <param name="startDay">The start date the picker shows.</param>
    /// <param name="inclusiveEndDay">
    /// The LAST day of the event as the picker shows it. Google stores an all-day end as the
    /// EXCLUSIVE next-day boundary, so this comes back as <paramref name="inclusiveEndDay"/> + 1 day.
    /// </param>
    /// <remarks>
    /// FHQ-174. The toggle has to re-derive both boundaries, not just re-route the getters: the model
    /// still holds the timed instants it was opened with (09:00 local for a create), and without this
    /// a user who taps a day, turns All Day on and saves stores 08:00Z rather than midnight UTC — the
    /// exact day-shift hazard this ticket removes, reintroduced by the most common path through the
    /// modal. The visible dates are preserved across the toggle; only the time component is dropped.
    /// </remarks>
    public static (DateTime Start, DateTime End) AllDayWallClocks(DateTime startDay, DateTime inclusiveEndDay)
    {
        var start = startDay.Date;
        var lastDay = inclusiveEndDay.Date < start ? start : inclusiveEndDay.Date;
        return (start, lastDay.AddDays(1));
    }

    /// <summary>
    /// The wall clocks the all-day toggle must store when it is switched OFF, given the two dates the
    /// pickers are currently showing and the times of day to restore.
    /// </summary>
    /// <param name="startDay">The start date the picker shows.</param>
    /// <param name="inclusiveEndDay">The last day of the event as the picker shows it.</param>
    /// <param name="startTimeOfDay">
    /// The start time to restore — the one the event had before it was switched to all-day, or
    /// <see cref="DefaultStartTimeOfDay"/> when it has never been timed in this modal session.
    /// </param>
    /// <param name="endTimeOfDay">The end time to restore, on the same basis.</param>
    /// <remarks>
    /// FHQ-174. Switching all-day OFF is a deliberate choice, not an inverse: the DATES the user can
    /// see stay exactly as they are, and only the times are restored. Reading the stored boundaries
    /// back would give 00:00–00:00 (a zero-length event on the wrong day once the exclusive end is
    /// accounted for), which is the "nonsense time" outcome. A derived end that is not after the
    /// start collapses to <see cref="FallbackTimedDuration"/> after the start, matching
    /// <c>SyncEndDateIfNeeded</c>.
    /// </remarks>
    public static (DateTime Start, DateTime End) TimedWallClocks(
        DateTime startDay, DateTime inclusiveEndDay, TimeSpan startTimeOfDay, TimeSpan endTimeOfDay)
    {
        var start = startDay.Date + startTimeOfDay;
        var end = inclusiveEndDay.Date + endTimeOfDay;
        return (start, end > start ? end : start + FallbackTimedDuration);
    }
}
