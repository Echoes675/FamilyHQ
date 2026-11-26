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
/// shape carries the event once instead, so the row exists exactly where <see cref="EventDate"/>
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
/// usually fired, so excluding a row once nothing is pending would empty the timeline of today's
/// events exactly when the family most needs them, and would drop an 18:00 event with one two-hour
/// reminder off the kiosk at 16:00. The row therefore stays until <see cref="EventDate"/> leaves
/// the window.
/// </para>
/// <para>
/// <b>There is no instant on this row, deliberately.</b> It used to carry the event's own
/// <c>DateTimeOffset</c> start and an is-all-day flag, and the client derived a day from them —
/// which is how an all-day event stored at midnight UTC came to read a day early in any
/// negative-offset zone, and how the client's idea of "today" came to disagree with the window the
/// server had bounded. The day is now decided once, server-side, and sent. Keeping the instant
/// beside it would invite exactly the second derivation this shape removes, and nothing would read
/// it: a field with no reader is the one most likely to be trusted wrongly later.
/// </para>
/// </remarks>
/// <param name="EventId">The event this row describes.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventDate">
/// The day the event is on, already decided by the one rule that decides it
/// (<c>FamilyHQ.Core.Calendar.EventWallClock</c>): the stored UTC date for an all-day event, the
/// household-zone date for a timed one. What the timeline files this row by, what its leading "when"
/// reads, what the Reminders tab counts, and the day a tap on the row opens.
/// </param>
/// <param name="EventTimeOfDay">
/// The event's start as the family reads it, in the household zone — or <b>null</b> for an all-day
/// event, which is the only all-day signal the client gets and all it needs: a null renders "all day"
/// in place of a time.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventDto(
    Guid EventId,
    string EventTitle,
    DateOnly EventDate,
    TimeOnly? EventTimeOfDay,
    IReadOnlyList<ReminderMemberDto> Members);
