namespace FamilyHQ.Core.DTOs;

/// <summary>
/// One row of the reminders timeline: an event that has at least one reminder due to fire, filed by
/// when the EVENT itself happens rather than by any one reminder's own trigger instant.
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
/// How many of this event's reminders have not yet fired. Never zero — an event with none left
/// upcoming produces no row at all (see <c>RemindersController.GetUpcoming</c>), the same rule that
/// already excluded a never-synced or explicitly-empty event.
/// </param>
/// <param name="NextReminderAt">The absolute instant the soonest still-upcoming reminder fires.</param>
/// <param name="NextReminderMinutes">
/// The stored offset the soonest still-upcoming reminder was computed from, so the row can describe
/// its lead time in the event's own terms
/// (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.Lead"/>) without re-deriving it
/// from <see cref="EventStart"/> and <see cref="NextReminderAt"/>.
/// </param>
/// <param name="NextReminderMethod">
/// The soonest still-upcoming reminder's delivery method — <c>"popup"</c> or <c>"email"</c> in
/// practice, but carried verbatim for the same reason <c>EventReminder.Method</c> is.
/// </param>
/// <param name="IsDefault">
/// Whether this event's reminders come from the calendar's default rather than overrides of its own.
/// One value for the whole event: <c>ReminderPingCalculator.EffectiveReminders</c> resolves inheritance
/// once per event, so every one of its pings agrees on this.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventDto(
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    int ReminderCount,
    DateTimeOffset NextReminderAt,
    int NextReminderMinutes,
    string NextReminderMethod,
    bool IsDefault,
    IReadOnlyList<ReminderMemberDto> Members);
