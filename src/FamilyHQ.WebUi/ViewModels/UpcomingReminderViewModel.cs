namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One row in the Reminders timeline: a single phone notification Google will fire for an event,
/// already resolved to the absolute instant it fires at. Whoever assembles the timeline builds this
/// from an event's own reminders (or the calendar's default, when the event follows it) — this type
/// carries only what a row needs to render, not Google's own reminder shape.
/// </summary>
/// <param name="TriggerAt">
/// The absolute instant the phone notification fires. What
/// <see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderBucketing.File{T}"/> files this row by, and
/// what the row's own ping-time column shows.
/// </param>
/// <param name="Method">
/// Google's delivery method for this one ping — <c>"popup"</c> or <c>"email"</c> in practice, but
/// carried verbatim for the same reason <c>EventReminder.Method</c> is: a method Google did not name
/// still has to round-trip to the screen.
/// </param>
/// <param name="Minutes">
/// The stored offset this ping was computed from, kept alongside <see cref="TriggerAt"/> so the row
/// can describe the lead time in the event's own terms
/// (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.Lead"/>) without re-deriving it
/// from two absolute instants.
/// </param>
/// <param name="IsDefault">
/// Whether this ping came from the calendar's default reminders rather than from an override the
/// event carries itself.
/// </param>
/// <param name="EventId">The event this ping belongs to — what tapping the row opens.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventStart">The event's own start, so the row can show it alongside the ping.</param>
/// <param name="EventIsAllDay">
/// Whether the event is all-day, which decides how <see cref="EventStart"/> is displayed.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderViewModel(
    DateTimeOffset TriggerAt,
    string Method,
    int Minutes,
    bool IsDefault,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    IReadOnlyList<ReminderMemberViewModel> Members);
