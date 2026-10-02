using System.Globalization;
using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.ViewModels;

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
/// is the opposite: a dense list of pings on a wall display, glanced at rather than read, where a
/// full sentence per row ("30 minutes before · Notification") would not fit two columns of them.
/// <see cref="Lead"/> therefore uses the compact unit words a form never would ("min"/"hr"/"day")
/// but keeps the <i>same</i> "largest unit that divides evenly" cascade
/// <see cref="ReminderDescription.Timing"/> uses, so a reminder is never reported as a different
/// number of units in the two places — only with a shorter word for the same one. That shared
/// cascade is the part that would actually create the hazard if it drifted, which is why it is
/// called out here rather than left to be noticed by diffing the two files.
/// </para>
/// <para>
/// <b>What <see cref="Lead"/> deliberately does not do.</b> It takes raw minutes, not an
/// <c>isAllDay</c> flag — unlike <see cref="ReminderDescription.Timing"/>, it never reinterprets the
/// value as "N days before local midnight". An all-day event's stored offset counts backwards from
/// midnight on the event's first day, not from the event's start, so a ping built from one has to
/// arrive here with a <see cref="UpcomingReminderViewModel.Minutes"/> that already means "minutes
/// before the start" in the sense this class assumes — otherwise <see cref="Lead"/> reports a lead
/// time that is arithmetically consistent but answers the wrong question. This type has no way to
/// detect that case itself, because <see cref="UpcomingReminderViewModel"/> carries nothing that
/// would let it recompute the value a different way. Flagged for whoever wires real data into this
/// view: either normalise an all-day ping's minutes before it reaches here, or confirm the pipeline
/// never needs to (same-day all-day reminders are excluded already — see the timeline's standing
/// footnote — but a multi-day-before all-day reminder is not).
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
    /// How long before the event this ping fires, in the compact units a timeline row has room for:
    /// "30 min before", "2 hrs before", "1 day before". Uses the largest unit that divides the value
    /// evenly, so a reminder set in minutes never reads as an approximate number of hours or days.
    /// </summary>
    public static string Lead(int minutes) => minutes switch
    {
        // Not reachable from the kiosk's own reminder form (its floor is the event's start, i.e.
        // zero), but a phone can set this, and Google accepts it — see EventReminder.Minutes.
        0 => "at the start",
        < 0 => $"{(-minutes).ToString(CultureInfo.InvariantCulture)} min after start",
        _ when minutes % MinutesPerWeek == 0 => $"{Count(minutes / MinutesPerWeek, "wk", "wks")} before",
        _ when minutes % MinutesPerDay == 0 => $"{Count(minutes / MinutesPerDay, "day", "days")} before",
        _ when minutes % MinutesPerHour == 0 => $"{Count(minutes / MinutesPerHour, "hr", "hrs")} before",
        // "min" is not pluralised — "47 min", not "47 mins" — matching how the word is actually used
        // when written this short.
        _ => $"{minutes.ToString(CultureInfo.InvariantCulture)} min before"
    };

    /// <summary>
    /// When the event itself starts, in the row's compact form: "starts 14:00". For an all-day
    /// event this is "all day" rather than the event's stored midnight boundary read as a time —
    /// rendering that literally would tell the family the event "starts 00:00", which is not a thing
    /// that happens.
    /// </summary>
    /// <remarks>
    /// Formats <paramref name="start"/>'s own embedded offset directly rather than converting it to
    /// any zone — the value is expected to arrive already wall-clock-correct for the household, the
    /// same way the rest of the dashboard's event tiles receive their start times. Reinterpreting it
    /// against a second zone here would risk showing a different time than the row's own ping-time
    /// column, which is read from the same kind of value.
    /// </remarks>
    public static string StartsAt(DateTimeOffset start, bool isAllDay) =>
        isAllDay ? "all day" : $"starts {start.ToString("HH:mm", CultureInfo.InvariantCulture)}";

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

    /// <summary>
    /// Who the ping is for: every member's name, joined — never "Family" or "Shared", because the
    /// row exists to answer whose phone is about to go off. Falls back to
    /// <paramref name="ownerCalendarName"/> only when no member is known at all.
    /// </summary>
    public static string People(IReadOnlyList<ReminderMemberViewModel> members, string ownerCalendarName) =>
        members.Count == 0 ? ownerCalendarName : string.Join(" · ", members.Select(m => m.DisplayName));

    private static string Count(int amount, string singular, string plural) =>
        $"{amount.ToString(CultureInfo.InvariantCulture)} {(amount == 1 ? singular : plural)}";
}
