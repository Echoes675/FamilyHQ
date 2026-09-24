namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// What a smoke scenario asks the kiosk to create. Everything is stated explicitly — the date, both
/// times, the member calendars — because a smoke scenario that leaned on the modal's defaults would
/// assert whatever those defaults happen to be on the day, and the point is to assert what Google
/// received.
/// </summary>
/// <param name="Title">The title, already carrying the scenario's short correlation id.</param>
/// <param name="Description">The description, already carrying the scenario's correlation marker.</param>
/// <param name="CalendarNames">The member calendars to select. One name for a single-member event, two for a shared one.</param>
/// <param name="Date">The date, in the family's zone.</param>
/// <param name="StartTime">Start wall-clock time in the family's zone.</param>
/// <param name="EndTime">End wall-clock time in the family's zone.</param>
/// <param name="Location">Optional location text.</param>
/// <param name="Recurrence">A bounded weekly repeat, or null for a single event.</param>
public sealed record SmokeEventDraft(
    string Title,
    string Description,
    IReadOnlyList<string> CalendarNames,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Location = null,
    SmokeWeeklyRecurrence? Recurrence = null);
