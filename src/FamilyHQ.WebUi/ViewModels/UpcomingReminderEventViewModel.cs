namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One row in the Reminders timeline: an event that HAS reminders, filed by when the EVENT happens
/// rather than by any one reminder's own trigger instant. Mirrors
/// <see cref="FamilyHQ.Core.DTOs.UpcomingReminderEventDto"/> field for field — see its own remarks for
/// why the row carries only the soonest upcoming reminder rather than every one, and why that one is
/// nullable.
/// </summary>
/// <param name="EventId">The event this row describes — what tapping it opens.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventStart">
/// The event's own start. What <see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderBucketing.File{T}"/>
/// files this row by, and what the row leads with in place of a ping time.
/// </param>
/// <param name="EventIsAllDay">
/// Whether the event is all-day, which decides how <see cref="EventStart"/> is displayed.
/// </param>
/// <param name="ReminderCount">
/// How many reminders the event carries in total, fired or not — not how many are left. See the
/// DTO's own parameter docs for why the total is the honest number.
/// </param>
/// <param name="NextReminderAt">
/// The absolute instant the soonest reminder that has not yet fired goes off, or null once they all
/// have. The row still renders; it reads "all sent" instead.
/// </param>
/// <param name="NextReminderMinutes">
/// The stored offset the soonest still-upcoming reminder was computed from, so the row can describe
/// its lead time in the event's own terms
/// (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.Lead"/>). Null exactly when
/// <see cref="NextReminderAt"/> is.
/// </param>
/// <param name="NextReminderMethod">
/// The soonest still-upcoming reminder's delivery method. Null exactly when
/// <see cref="NextReminderAt"/> is, and the row then shows no method glyph.
/// </param>
/// <param name="IsDefault">
/// Whether this event's reminders come from the calendar's default rather than overrides of its own.
/// Known for the whole event, so still answerable once every reminder has fired.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventViewModel(
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    int ReminderCount,
    DateTimeOffset? NextReminderAt,
    int? NextReminderMinutes,
    string? NextReminderMethod,
    bool IsDefault,
    IReadOnlyList<ReminderMemberViewModel> Members);
