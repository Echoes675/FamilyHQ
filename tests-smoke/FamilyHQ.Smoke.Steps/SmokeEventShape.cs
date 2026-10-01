using System.Globalization;
using FamilyHQ.Smoke.Common.Correlation;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The shape every smoke event has, in one place.
/// <para>
/// A fixed mid-morning slot, for an hour, in the family's zone: a day-view assertion is then never fighting
/// the current-time line or a scroll position. Timed, not all-day, unless a scenario says otherwise — an
/// all-day event carries no <c>timeZone</c>, and the zone a series is anchored to is one of the things this
/// suite exists to check.
/// </para>
/// <para>
/// The <b>time</b> is shared by everything; the <b>day</b> is not. Each scenario puts its events on a day of
/// its own (<see cref="SmokeScenarioDays"/>), and no day is named here at all. Sharing one would be the
/// obvious thing to do and is what this suite did first: it survives while the suite is small and a run is
/// rare, and then the day view fills with tiles that sit over one another, the click for the tile a scenario
/// wants stops landing, and the failure looks like a product fault rather than the suite crowding itself out.
/// </para>
/// </summary>
public static class SmokeEventShape
{
    /// <summary>The shape of Google's all-day <c>date</c> field — an RFC 3339 full-date.</summary>
    public const string GoogleDateFormat = "yyyy-MM-dd";

    public static TimeOnly StartTime => new(10, 0);

    public static TimeOnly EndTime => new(11, 0);

    /// <summary>
    /// The first <paramref name="weekday"/> on or after <paramref name="from"/>, for a scenario whose rule
    /// names a weekday: Google's expansion then starts where the rule says it should rather than on whichever
    /// day the run happens to be.
    /// </summary>
    public static DateOnly NextDate(DateOnly from, DayOfWeek weekday)
    {
        var date = from;
        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

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
    /// Google's all-day boundary for <paramref name="date"/>: a bare calendar date, with no instant and
    /// no zone.
    /// <para>
    /// The absence of a zone is the semantics, not an omission — an all-day event is anchored to a date,
    /// so it cannot be moved by a daylight-saving change and there is nothing for a zone to mean.
    /// </para>
    /// </summary>
    public static GoogleEventDateTime GoogleAllDayBoundary(DateOnly date) =>
        new(
            DateTime: null,
            Date: date.ToString(GoogleDateFormat, CultureInfo.InvariantCulture),
            TimeZone: null);

    /// <summary>
    /// The exclusive end date Google expects for an all-day event whose last day is
    /// <paramref name="lastDay"/> — the day <i>after</i> it.
    /// <para>
    /// Stated here rather than inline at each use because it is the single most easily mis-stated thing
    /// about an all-day event: an end date of the last day itself describes an event that is over before
    /// it starts, and Google rejects it.
    /// </para>
    /// </summary>
    public static DateOnly ExclusiveEndDate(DateOnly lastDay) => lastDay.AddDays(1);

    /// <summary>
    /// An event drafted for Google the way a phone would create one: free-text description, a location, a
    /// colour and an explicit reminder. Every one of those is a field the golden-rule scenario expects to
    /// find untouched after the kiosk edits something else.
    /// </summary>
    /// <param name="reminderOverrides">
    /// The exact reminder set to put on the event, for a scenario whose subject is the reminders
    /// themselves. When null the single <paramref name="reminderMinutes"/> notification is used, which is
    /// all the golden-rule probe needs.
    /// </param>
    public static GoogleEventDraft PhoneStyleDraft(
        SmokeCorrelation correlation,
        string baseTitle,
        string descriptionText,
        DateOnly date,
        string? colorId = "5",
        int reminderMinutes = 45,
        IReadOnlyList<string>? recurrence = null,
        IReadOnlyList<GoogleEventReminderOverride>? reminderOverrides = null) =>
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
                Overrides: reminderOverrides
                           ?? [new GoogleEventReminderOverride("popup", reminderMinutes)]));

    /// <summary>
    /// The same, for a single-day all-day event: a bare start date and Google's exclusive end date, with
    /// no time and no zone anywhere.
    /// </summary>
    public static GoogleEventDraft PhoneStyleAllDayDraft(
        SmokeCorrelation correlation,
        string baseTitle,
        string descriptionText,
        DateOnly date,
        IReadOnlyList<string>? recurrence = null) =>
        new(
            Summary: correlation.Title(baseTitle),
            Start: GoogleAllDayBoundary(date),
            End: GoogleAllDayBoundary(ExclusiveEndDate(date)),
            Description: correlation.Description(descriptionText),
            Location: "Smoke Test Venue",
            ColorId: "5",
            Recurrence: recurrence,
            Reminders: new GoogleEventReminders(
                UseDefault: false,
                Overrides: [new GoogleEventReminderOverride("popup", 45)]));
}
