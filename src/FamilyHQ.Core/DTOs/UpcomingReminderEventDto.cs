namespace FamilyHQ.Core.DTOs;

/// <summary>
/// One row of the reminders timeline: an event that HAS reminders, filed by when the EVENT itself
/// happens rather than by any one reminder's own trigger instant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one row per event, not one per ping.</b> An event carrying several reminders — the family's
/// own example is one every day for the week before it — used to produce one row per ping, scattered
/// across whichever section each trigger instant happened to land in. A single event then appeared
/// repeatedly, under headings that did not agree with each other about when it actually was. This
/// shape carries the event once and summarises its reminders (<see cref="ReminderCount"/>,
/// <see cref="NextReminderAt"/>) instead, so the row exists exactly where <see cref="EventStart"/>
/// files it.
/// </para>
/// <para>
/// <b>Only the next reminder is carried, not every one.</b> The timeline is fetched whole and every
/// field here is paid for once per event rather than once per ping, which is most of the saving this
/// shape exists for — but it does mean the client cannot locally recompute
/// <see cref="NextReminderAt"/> once it passes; a fresh fetch is required instead (see the reminders
/// timeline's per-minute tick in <c>Index.razor</c>). Carrying every ping's trigger would avoid that
/// refetch at the cost of reintroducing the per-ping payload this shape was built to drop.
/// </para>
/// <para>
/// <b>A row outlives its own reminders.</b> The three "next reminder" values are nullable because a
/// row is not evidence that a notification is still coming — it is evidence that the event has
/// reminders. By the time an event starts its reminders have all usually fired, so excluding a row
/// once nothing is pending would empty the Today section exactly when the family most needs it, and
/// would drop an 18:00 event with one two-hour reminder off the kiosk at 16:00. The row therefore
/// stays until <see cref="EventStart"/> leaves the window, and says "all sent" instead.
/// </para>
/// </remarks>
/// <param name="EventId">The event this row describes — what tapping it opens.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventStart">
/// The event's own start. What the reminders timeline files this row by (never a reminder's trigger
/// instant), and shown on the row in place of the ping time a per-ping row used to lead with.
/// </param>
/// <param name="EventIsAllDay">
/// Whether the event is all-day, which decides how <see cref="EventStart"/> is displayed ("all day"
/// rather than a time).
/// </param>
/// <param name="ReminderCount">
/// How many reminders the event carries in TOTAL, fired or not — the number the family actually set
/// in Google, which is the system of record. Deliberately not a count of the ones still to come: a
/// decaying count reads as though reminders had gone missing ("2 reminders" on day five of an event
/// with one every day for a week), and it would disagree with what the event modal's own Reminders
/// tab shows for the same event. Never zero: an event that will produce no notification at all
/// produces no row either (see <c>RemindersController.RowFor</c>), which is what keeps a never-synced
/// event distinct from one whose reminders were removed.
/// </param>
/// <param name="NextReminderAt">
/// The absolute instant the soonest reminder that has NOT yet fired goes off, or null once every one
/// of them has. Null is an ordinary end state rather than missing data — see the remarks on why the
/// row stays regardless.
/// </param>
/// <param name="NextReminderMinutes">
/// The stored offset the soonest still-upcoming reminder was computed from, so the row can describe
/// its lead time in the event's own terms (<c>ReminderRowDisplay.Lead</c>) without re-deriving it
/// from <see cref="EventStart"/> and <see cref="NextReminderAt"/>. Null exactly when
/// <see cref="NextReminderAt"/> is.
/// </param>
/// <param name="NextReminderMethod">
/// The soonest still-upcoming reminder's delivery method — <c>"popup"</c> or <c>"email"</c> in
/// practice, but carried verbatim for the same reason <c>EventReminder.Method</c> is. Null exactly
/// when <see cref="NextReminderAt"/> is, and the row then renders no method glyph rather than a
/// stand-in for one.
/// </param>
/// <param name="IsDefault">
/// Whether this event's reminders come from the calendar's default rather than overrides of its own.
/// One value for the whole event, and known even when nothing is still pending:
/// <c>ReminderPingCalculator.EffectiveReminders</c> resolves inheritance once per event, so every one
/// of its pings agrees on this and any of them can be asked.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventDto(
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    int ReminderCount,
    DateTimeOffset? NextReminderAt,
    int? NextReminderMinutes,
    string? NextReminderMethod,
    bool IsDefault,
    IReadOnlyList<ReminderMemberDto> Members);
