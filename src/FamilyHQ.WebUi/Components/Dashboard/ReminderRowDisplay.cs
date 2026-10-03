using System.Globalization;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The one place a Reminders-timeline row's "when" is decided — both the text the row leads with and
/// the day a tap on it opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is its own class rather than a <c>.razor</c> <c>@code</c> block.</b> There is no bUnit
/// in this repo, so anything a Razor component computes for itself is computed where no unit test can
/// reach it — the same reason <see cref="RemindersViewLogic"/> exists beside
/// <see cref="RemindersView"/>. A row's displayed time was once formatted in raw UTC while every
/// other view converted to the household zone, and it shipped: that is the class of mistake this file
/// exists to keep testable.
/// </para>
/// <para>
/// <b>The row says nothing about the reminders.</b> It reads "[when] · title · [who]". The bell glyph
/// and the "1 reminder · next 45 min before" line were removed at the family's request, and with them
/// the lead-time wording this class used to hold — the compact "30 min before" / "2 hrs before" form
/// and its all-day special case. <c>ReminderDescription</c> is now the only renderer of a reminder's
/// timing, which is where a family member reads it anyway: in the event modal, carefully, rather than
/// at a glance across a wall display.
/// </para>
/// </remarks>
public static class ReminderRowDisplay
{
    /// <summary>
    /// The row's leading "when": the event's own start as the family sees it. A timed event reads
    /// "14:00"; an all-day event reads "all day" rather than its stored midnight boundary read as a
    /// time, which would tell the family the event "starts 00:00".
    /// <para>
    /// With <paramref name="withDate"/> the day and date lead it — "Sat 4 Oct · 10:00",
    /// "Sat 4 Oct · all day" — which the three further-out sections need and the near two do not:
    /// This week, This month and Next month each span several days, so three occurrences of one
    /// recurring series on three different days otherwise render as three identical rows (the
    /// screenshot that prompted this showed exactly that). Today and Tomorrow span one day each and
    /// their own heading already names it.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The month is in the format on purpose, and <c>ddd d</c> alone would not do. This week is the
    /// one section whose span can cross a month boundary: with a Monday-start week and today on
    /// Monday 28 September, <see cref="ReminderBucketing.File{T}"/> gives it 30 September through 4
    /// October, so "Wed 30" and "Thu 1" sit in the same pane saying nothing about which month either
    /// is in. (This month and Next month are each bounded to one calendar month, so neither needs it
    /// to tell its own rows apart — but a row that reads correctly only once the reader has resolved
    /// it against the pane heading above is a row that reads wrongly at a glance, which is the only
    /// way this view is ever read.)
    /// </para>
    /// <para>
    /// <paramref name="start"/> arrives from Postgres via Npgsql as a <c>timestamptz</c> read back
    /// with a <c>+00:00</c> offset — not wall-clock-correct for the household, whatever offset it
    /// carries. Every other dashboard view converts before formatting
    /// (<see cref="FamilyHQ.WebUi.ViewModels.CalendarEventViewModelExtensions.StartLocal"/>), and this
    /// is why <paramref name="zone"/> is required rather than optional: formatting the raw value, as
    /// this used to, reads an hour early for every timed event for roughly half the year in a zone
    /// that observes DST.
    /// </para>
    /// <para>
    /// <see cref="CultureInfo.InvariantCulture"/> throughout, so the kiosk's own locale cannot reorder
    /// the date or translate the day name, and so CI — which runs globalization-invariant — formats it
    /// the same way the Pi does.
    /// </para>
    /// </remarks>
    public static string EventTime(DateTimeOffset start, bool isAllDay, TimeZoneInfo zone, bool withDate)
    {
        var local = TimeZoneInfo.ConvertTime(start, zone);
        var when = isAllDay ? "all day" : local.ToString("HH:mm", CultureInfo.InvariantCulture);

        return withDate
            ? $"{local.ToString("ddd d MMM", CultureInfo.InvariantCulture)} · {when}"
            : when;
    }

    /// <summary>
    /// The row's own day: the date a tap on it drills into. Read through the same conversion
    /// <see cref="EventTime"/> formats and <see cref="ReminderBucketing.File{T}"/> files by, so the
    /// date the row shows, the section it sits under and the day the tap opens cannot disagree — a
    /// late-evening event read in UTC instead would drill into the day before the one on the row.
    /// </summary>
    public static DateTime LocalDate(DateTimeOffset start, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(start, zone).Date;
}
