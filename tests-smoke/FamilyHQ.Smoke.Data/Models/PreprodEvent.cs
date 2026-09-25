namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// An event as preprod serves it to the kiosk. The member list is the interesting part: it is
/// FamilyHQ's answer to "whose event is this?", which is exactly what the Google-to-kiosk scenarios
/// assert. Note that the description arrives with the managed <c>[members: …]</c> tag already stripped.
/// </summary>
public sealed record PreprodEvent(
    Guid Id,
    string GoogleEventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<PreprodCalendar> Members,
    bool IsRecurring,
    string? RecurrenceRule);
