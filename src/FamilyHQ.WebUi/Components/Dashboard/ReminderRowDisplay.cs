using System.Globalization;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The one place the text a Reminders-timeline row leads with is decided.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is its own class rather than a <c>.razor</c> <c>@code</c> block.</b> There is no bUnit
/// in this repo, so anything a Razor component computes for itself is computed where no unit test can
/// reach it — the same reason <see cref="RemindersViewLogic"/> exists beside
/// <see cref="RemindersView"/>. A row's displayed time was once formatted in raw UTC while every
/// other view converted to the household zone, and it shipped: that is the class of mistake this file
/// exists to keep testable. The derivation it describes has since moved to the server, which applies
/// one rule to every event and sends the day and the time of day it decided, so what is left here is
/// the formatting and the Today/Tomorrow choice.
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
    /// The row's leading "when": the day the event falls on, then its start as the family sees it —
    /// "Today · 14:00", "Tomorrow · 21:30", "Sat 4 Oct · 10:00", "Today · all day".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both values arrive already decided. The server applies one rule to every event
    /// (<c>FamilyHQ.Core.Calendar.EventWallClock</c>) and sends the date and the time of day, so there
    /// is no zone here and nothing to convert: this class used to take a <see cref="TimeZoneInfo"/>
    /// and derive a day of its own, which is how an all-day event read a day early in any
    /// negative-offset zone and how the kiosk's idea of "today" came to disagree with the window the
    /// server had bounded.
    /// </para>
    /// <para>
    /// A null <paramref name="time"/> means all-day, and is the only all-day signal a row carries.
    /// 00:00 is a different fact and reads as a time, because a timed event starting at midnight
    /// exists.
    /// </para>
    /// <para>
    /// Every row names its day, because each panel spans a whole calendar month: without it, three
    /// occurrences of one recurring series on three different days render as three identical rows.
    /// "Today" and "Tomorrow" are substituted because they are the two days a family reads first, and
    /// they are read off the row's OWN date against <paramref name="today"/> — never off the panel
    /// holding it. On the last day of a month tomorrow is filed under "Next month" and must still read
    /// "Tomorrow" there. Hence the whole-date comparison rather than arithmetic on a day number: the
    /// month-end and year-end cases are exactly the ones a cheaper comparison gets wrong, and they are
    /// pinned by <c>ReminderRowDisplayTests</c>.
    /// </para>
    /// <para>
    /// A date before <paramref name="today"/> falls back to the dated form. There is deliberately no
    /// "Yesterday": <see cref="ReminderBucketing.File{T}"/> drops a past row, so the label would name
    /// a row that cannot be rendered.
    /// </para>
    /// <para>
    /// The month is in the format on purpose, and <c>ddd d</c> alone would not do: "This month" and
    /// "Next month" sit side by side, so the last row of one and the first row of the other are one
    /// glance apart, and "Wed 30" beside "Thu 1" says nothing about which month either is in.
    /// <see cref="CultureInfo.InvariantCulture"/> throughout, so the kiosk's own locale cannot reorder
    /// the date or translate the day name, and so CI — which runs globalization-invariant — formats it
    /// the same way the Pi does.
    /// </para>
    /// </remarks>
    public static string EventTime(DateOnly date, TimeOnly? time, DateOnly today)
    {
        var when = time is { } startsAt
            ? startsAt.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "all day";

        var day =
            date == today ? "Today"
            : date == today.AddDays(1) ? "Tomorrow"
            : date.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        return $"{day} · {when}";
    }
}
