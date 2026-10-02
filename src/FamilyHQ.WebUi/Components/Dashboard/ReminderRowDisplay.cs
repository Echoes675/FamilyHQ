using System.Globalization;
using FamilyHQ.Core.Validators;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Puts one Reminders-timeline row into words. Deliberately separate from
/// <see cref="ReminderDescription"/> rather than reusing it — see the remarks below for why that is a
/// considered choice, not an accidental duplicate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is its own class.</b> <see cref="ReminderDescription"/> exists to read correctly in
/// the edit modal's reminder list, which the family opens rarely and reads carefully. This timeline
/// is the opposite: a dense list of rows on a wall display, glanced at rather than read, where a
/// full sentence per row ("30 minutes before · Notification") would not fit two columns of them.
/// <see cref="Lead"/> therefore uses the compact unit words a form never would ("min"/"hr"/"day")
/// but keeps the <i>same</i> "largest unit that divides evenly" cascade
/// <see cref="ReminderDescription.Timing"/> uses, so a reminder is never reported as a different
/// number of units in the two places — only with a shorter word for the same one. That shared
/// cascade is the part that would actually create the hazard if it drifted, which is why it is
/// called out here rather than left to be noticed by diffing the two files.
/// </para>
/// <para>
/// <b>Where correctness outranks compactness.</b> An all-day event's stored offset counts backwards
/// from midnight on the event's first day, not from the event's start, so the same "divide by 60"
/// reading <see cref="Lead"/> uses for a timed event answers the wrong question for one — 420
/// minutes is "7 hrs before" by that arithmetic, but it is actually "1 day before at 17:00". There
/// is no compact form of that second sentence worth inventing, so <see cref="Lead"/> does not try:
/// for an all-day event it hands the value straight to
/// <see cref="ReminderDescription.Timing(int, bool)"/>, which already gets this right via
/// <see cref="ReminderPickerModel.ToDaysBeforeAndTime"/>, and returns whatever that says verbatim.
/// The tricky arithmetic stays defined in exactly one place either way.
/// </para>
/// </remarks>
public static class ReminderRowDisplay
{
    private const int MinutesPerHour = 60;

    // Shared with ReminderDescription, which defines the same two constants off the same source —
    // see ReminderPickerModel.MinutesPerDay — so the day/week boundary cannot drift between the two
    // renderers even though the words either side of it are deliberately different.
    private const int MinutesPerDay = ReminderPickerModel.MinutesPerDay;
    private const int MinutesPerWeek = 7 * ReminderPickerModel.MinutesPerDay;

    /// <summary>
    /// How long before the event this ping fires. For a timed event, the compact units a timeline
    /// row has room for: "30 min before", "2 hrs before", "1 day before" — the largest unit that
    /// divides the value evenly, so a reminder set in minutes never reads as an approximate number
    /// of hours or days. For an all-day event, <paramref name="minutes"/> counts backwards from
    /// local midnight rather than from the start, so this delegates to
    /// <see cref="ReminderDescription.Timing(int, bool)"/> instead of reading it the timed way —
    /// see the class remarks for why "7 hrs before" would be the wrong answer for the same value.
    /// </summary>
    public static string Lead(int minutes, bool isAllDay)
    {
        if (isAllDay)
        {
            return ReminderDescription.Timing(minutes, isAllDay: true);
        }

        return minutes switch
        {
            // Not reachable from the kiosk's own reminder form (its floor is the event's start,
            // i.e. zero), but a phone can set this, and Google accepts it — see
            // EventReminder.Minutes.
            0 => "at the start",
            < 0 => $"{(-minutes).ToString(CultureInfo.InvariantCulture)} min after start",
            _ when minutes % MinutesPerWeek == 0 => $"{Count(minutes / MinutesPerWeek, "wk", "wks")} before",
            _ when minutes % MinutesPerDay == 0 => $"{Count(minutes / MinutesPerDay, "day", "days")} before",
            _ when minutes % MinutesPerHour == 0 => $"{Count(minutes / MinutesPerHour, "hr", "hrs")} before",
            // "min" is not pluralised — "47 min", not "47 mins" — matching how the word is
            // actually used when written this short.
            _ => $"{minutes.ToString(CultureInfo.InvariantCulture)} min before"
        };
    }

    /// <summary>
    /// The event's own start, in the row's compact leading form: "14:00" — or "all day" for an
    /// all-day event, rather than its stored midnight boundary read as a time, which would tell the
    /// family the event "starts 00:00". This is what the row leads with where a per-ping row used to
    /// lead with the ping's own trigger time — see the class remarks on why the row is filed, and now
    /// labelled, by the event rather than by any one of its reminders.
    /// </summary>
    /// <remarks>
    /// <paramref name="start"/> arrives from Postgres via Npgsql as a <c>timestamptz</c> read back
    /// with a <c>+00:00</c> offset — not wall-clock-correct for the household, whatever offset it
    /// carries. Every other dashboard view converts before formatting
    /// (<see cref="FamilyHQ.WebUi.ViewModels.CalendarEventViewModelExtensions.StartLocal"/>), and this
    /// is why <paramref name="zone"/> is required rather than optional: formatting the raw value, as
    /// this used to, reads an hour early for every timed event for roughly half the year in a zone
    /// that observes DST.
    /// </remarks>
    public static string EventTime(DateTimeOffset start, bool isAllDay, TimeZoneInfo zone) =>
        isAllDay ? "all day" : TimeZoneInfo.ConvertTime(start, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The row's reminder summary: how many of the event's reminders are still due to fire, and the
    /// lead time of the soonest one — "3 reminders · next 2 hrs before" (singular: "1 reminder · next
    /// …"). Delegates the lead-time wording to <see cref="Lead"/> rather than re-deriving it, so the
    /// two can never disagree about the same reminder.
    /// </summary>
    public static string ReminderSummary(int reminderCount, int nextReminderMinutes, bool isAllDay)
    {
        var noun = reminderCount == 1 ? "reminder" : "reminders";
        return $"{reminderCount.ToString(CultureInfo.InvariantCulture)} {noun} · next {Lead(nextReminderMinutes, isAllDay)}";
    }

    /// <summary>
    /// A one-glyph hint at how the ping is delivered. Falls back to a plain bullet for a method
    /// Google did not name and the kiosk's own form cannot create — the row still has to render
    /// something; a method this cannot picture is not a reason to leave it blank or throw.
    /// </summary>
    public static string MethodIcon(string method) => method switch
    {
        EventRemindersValidator.PopupMethod => "🔔",
        EventRemindersValidator.EmailMethod => "✉",
        _ => "•"
    };

    private static string Count(int amount, string singular, string plural) =>
        $"{amount.ToString(CultureInfo.InvariantCulture)} {(amount == 1 ? singular : plural)}";
}
