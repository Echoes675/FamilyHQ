namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One row in the Reminders timeline: an event whose reminders were set ON THE EVENT, filed by when
/// the EVENT happens rather than by any one reminder's own trigger instant. Mirrors
/// <see cref="FamilyHQ.Core.DTOs.UpcomingReminderEventDto"/> field for field — see its own remarks for
/// why a row carries nothing describing the reminders it exists because of.
/// </summary>
/// <param name="EventId">The event this row describes.</param>
/// <param name="EventTitle">The event's title, for the row's label.</param>
/// <param name="EventStart">
/// The event's own start. What <see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderBucketing.File{T}"/>
/// files this row by, what the row's leading "when" is read from
/// (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.EventTime"/>), and the day a tap
/// on the row opens (<see cref="FamilyHQ.WebUi.Components.Dashboard.ReminderRowDisplay.LocalDate"/>).
/// </param>
/// <param name="EventIsAllDay">
/// Whether the event is all-day, which decides how <see cref="EventStart"/> is displayed.
/// </param>
/// <param name="Members">The people the event is shared with, for the row's chips.</param>
public sealed record UpcomingReminderEventViewModel(
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    IReadOnlyList<ReminderMemberViewModel> Members);
