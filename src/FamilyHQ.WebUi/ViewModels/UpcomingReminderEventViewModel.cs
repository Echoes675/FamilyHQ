namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One row in the Reminders timeline: an event whose reminders were set ON THE EVENT, filed by when
/// the EVENT happens rather than by any one reminder's own trigger instant. Mirrors
/// <see cref="FamilyHQ.Core.DTOs.UpcomingReminderEventDto"/> field for field — see its own remarks for
/// why a row carries nothing describing the reminders it exists because of, and no instant and no
/// zone either: which day the event is on is decided once, server-side, and sent.
/// </summary>
/// <param name="EventId">The event this row describes.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventDate">
/// The day the event is on, decided by the server and not re-derived here. What
/// <see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderBucketing.File{T}"/> files this row by, what
/// its leading "when" reads
/// (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.EventTime"/>), what the
/// Reminders tab counts, and the day a tap on the row opens.
/// </param>
/// <param name="EventTimeOfDay">
/// The event's start in the household zone, or null for an all-day event. Null is the only all-day
/// signal this row carries, and all it needs.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventViewModel(
    Guid EventId,
    string EventTitle,
    DateOnly EventDate,
    TimeOnly? EventTimeOfDay,
    IReadOnlyList<ReminderMemberViewModel> Members);
