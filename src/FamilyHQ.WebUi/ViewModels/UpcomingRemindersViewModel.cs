namespace FamilyHQ.WebUi.ViewModels;

/// <summary>
/// One fetch of the reminders timeline: the rows, the date they were filed against, and when that
/// date stops being today. Mirrors <see cref="FamilyHQ.Core.DTOs.UpcomingRemindersDto"/> field for
/// field.
/// </summary>
/// <remarks>
/// The anchor arrives with the rows rather than being read off the kiosk's clock, because the server
/// is what knows the household's zone. The kiosk's clock keeps two jobs, both about time PASSING
/// rather than about which day an event is on: noticing that
/// <see cref="NextRolloverUtc"/> has gone by so the rows can be refetched, and carrying the whole-day
/// offset the E2E suite shifts in lower environments.
/// </remarks>
/// <param name="Today">The household's current date, as the server computed it.</param>
/// <param name="NextRolloverUtc">The instant <paramref name="Today"/> stops being today.</param>
/// <param name="Rows">The rows, already in the order the view renders them.</param>
public sealed record UpcomingRemindersViewModel(
    DateOnly Today,
    DateTimeOffset NextRolloverUtc,
    IReadOnlyList<UpcomingReminderEventViewModel> Rows);
