namespace FamilyHQ.Core.DTOs;

/// <summary>
/// One row of the reminders timeline: an event whose reminders were set ON THE EVENT, filed by when
/// the EVENT itself happens rather than by any one reminder's own trigger instant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one row per event, not one per ping.</b> An event carrying several reminders — the family's
/// own example is one every day for the week before it — used to produce one row per ping, scattered
/// across whichever section each trigger instant happened to land in. A single event then appeared
/// repeatedly, under headings that did not agree with each other about when it actually was. This
/// shape carries the event once instead, so the row exists exactly where <see cref="EventStart"/>
/// files it.
/// </para>
/// <para>
/// <b>Nothing here describes the reminders themselves.</b> The row reads "[when] · title · [who]":
/// the family asked for the bell glyph and the "1 reminder · next 45 min before" line to go, because
/// on a wall display they crowded out the three things a row is glanced at for. What makes this a
/// reminders view is therefore the FILTER — only events whose reminders were set on the event reach
/// it — and not any field on the row. The count, the next reminder's instant, its stored offset and
/// its delivery method were all carried here for that removed second line and the glyph, and were
/// removed with them rather than left unrendered: a field with no reader is the one most likely to be
/// trusted wrongly later. The event modal remains where a family member reads what an event's
/// reminders actually are, and the four stored reminder states stay distinguishable in the model.
/// </para>
/// <para>
/// <b>There is no "inherited" row, so there is no field saying a row is one.</b> An event that merely
/// follows its calendar's usual reminders produces no row at all (see <c>RemindersController.RowFor</c>
/// for why that is the requirement rather than an oversight), so every row that exists carries
/// reminders somebody set on the event itself. A flag distinguishing the two could only ever hold one
/// value here, which is why this shape has none — it is not that inheritance stopped mattering, only
/// that it is settled before a row is built.
/// </para>
/// <para>
/// <b>A row outlives its own reminders.</b> A row is not evidence that a notification is still coming
/// — it is evidence that the event has reminders. By the time an event starts its reminders have all
/// usually fired, so excluding a row once nothing is pending would empty the Today section exactly
/// when the family most needs it, and would drop an 18:00 event with one two-hour reminder off the
/// kiosk at 16:00. The row therefore stays until <see cref="EventStart"/> leaves the window.
/// </para>
/// </remarks>
/// <param name="EventId">The event this row describes.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventStart">
/// The event's own start. What the reminders timeline files this row by (never a reminder's trigger
/// instant), what the row's leading "when" is read from, and the day a tap on the row opens.
/// </param>
/// <param name="EventIsAllDay">
/// Whether the event is all-day, which decides how <see cref="EventStart"/> is displayed ("all day"
/// rather than a time).
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventDto(
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    IReadOnlyList<ReminderMemberDto> Members);
