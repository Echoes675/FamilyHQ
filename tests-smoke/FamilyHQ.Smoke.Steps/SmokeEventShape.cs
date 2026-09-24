using FamilyHQ.Smoke.Common.Correlation;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The shape every smoke event has, in one place.
/// <para>
/// Tomorrow at 10:00 in the family's zone, for an hour. Tomorrow rather than today so a run that starts at
/// 23:55 does not create an event on a day the kiosk is about to roll off, and a fixed mid-morning slot so
/// a Day-view assertion is never fighting the current-time line or a scroll position. Timed, not all-day,
/// because an all-day event carries no <c>timeZone</c> and the time zone a series is anchored to is one of
/// the things this suite exists to check (FHQ-170).
/// </para>
/// </summary>
public static class SmokeEventShape
{
    public static TimeOnly StartTime => new(10, 0);

    public static TimeOnly EndTime => new(11, 0);

    /// <summary>The date every smoke event sits on: tomorrow, in the family's zone.</summary>
    public static DateOnly Date => FamilyClock.Today.AddDays(1);

    /// <summary>
    /// Google's boundary for a wall-clock time on <paramref name="date"/>, carrying the family's zone
    /// explicitly. Sending both the instant and the zone is what Google itself does, and it is what makes a
    /// series anchored rather than merely dated.
    /// </summary>
    public static GoogleEventDateTime GoogleBoundary(DateOnly date, TimeOnly time) =>
        new(
            FamilyClock.ToFamilyOffset(date.ToDateTime(time)),
            Date: null,
            TimeZone: FamilyClock.TimeZoneId);

    /// <summary>
    /// An event drafted for Google the way a phone would create one: free-text description, a location, a
    /// colour and an explicit reminder. Every one of those is a field the golden-rule scenario expects to
    /// find untouched after the kiosk edits something else.
    /// </summary>
    public static GoogleEventDraft PhoneStyleDraft(
        SmokeCorrelation correlation,
        string baseTitle,
        string descriptionText,
        DateOnly date,
        string? colorId = "5",
        int reminderMinutes = 45,
        IReadOnlyList<string>? recurrence = null) =>
        new(
            Summary: correlation.Title(baseTitle),
            Start: GoogleBoundary(date, StartTime),
            End: GoogleBoundary(date, EndTime),
            Description: correlation.Description(descriptionText),
            // A made-up venue on purpose: nothing this suite writes to a live calendar should resemble a
            // real address belonging to anyone.
            Location: "Smoke Test Venue",
            ColorId: colorId,
            Recurrence: recurrence,
            Reminders: new GoogleEventReminders(
                UseDefault: false,
                Overrides: [new GoogleEventReminderOverride("popup", reminderMinutes)]));
}
