namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Files a flat list of rows into the five sections the reminders timeline lays out in two columns,
/// by one instant read off each row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why Tomorrow is its own section.</b> Calendar buckets alone misfile it: on the last day of a
/// week, tomorrow belongs to next week, and on the last day of a month it belongs to next month — so
/// a row the family needs to see first would render in the far-right column. Matching Tomorrow
/// before either calendar bucket is what keeps "soon" meaning soon.
/// </para>
/// <para>
/// <b>The week start is a parameter.</b> It is a household preference, and this class has no
/// business knowing which one. Pass it in; test both.
/// </para>
/// <para>
/// <b>Which instant a row is filed by is the caller's decision, not this class's.</b> The timeline
/// files by the EVENT's own start (see <see cref="RemindersViewLogic.Sections"/>); this class is
/// generic over the row type and takes the instant as a selector so that choice lives in exactly one
/// place rather than being baked in here.
/// </para>
/// </remarks>
public static class ReminderBucketing
{
    /// <summary>
    /// Files <paramref name="rows"/> into the five sections, in match order Today → Tomorrow →
    /// This week → This month → Next month, and always returns all five — an absent section would
    /// shuffle the view's fixed two-column layout, and collapsing an empty one is the view's job, not
    /// this class's.
    /// </summary>
    /// <remarks>
    /// Sections are strictly non-overlapping: the first one a row's local day satisfies is the one it
    /// lands in, so <c>ThisWeek</c> begins the day after tomorrow and <c>ThisMonth</c> begins only
    /// after the week containing <paramref name="today"/> has ended. A row whose local day satisfies
    /// none of the five — beyond the window the server returned — is dropped rather than forced into
    /// the last section, where it would quietly misrepresent "next month".
    /// </remarks>
    /// <param name="rows">The rows to file. Never mutated or reordered in place.</param>
    /// <param name="instant">
    /// Reads the instant a row is filed and sorted by. A <c>Func</c> rather than a view-model
    /// dependency, so this class needs nothing about the row beyond the one value it files by.
    /// </param>
    /// <param name="today">The kiosk's current local date.</param>
    /// <param name="weekStart">
    /// The first day of the household's week. A parameter, never a constant: which day a week starts
    /// on is a household preference this class has no business assuming.
    /// </param>
    /// <param name="zone">
    /// The zone a row's local day is read in. A row's day is its day <i>in this zone</i>, not its
    /// UTC day — the two disagree for several hours around every midnight, and for an extra hour
    /// either side of a daylight-saving transition.
    /// </param>
    public static IReadOnlyList<ReminderSection<T>> File<T>(
        IEnumerable<T> rows,
        Func<T, DateTimeOffset> instant,
        DateOnly today,
        DayOfWeek weekStart,
        TimeZoneInfo zone)
    {
        var tomorrow = today.AddDays(1);
        var weekEnd = EndOfWeek(today, weekStart);
        var thisMonthEnd = EndOfMonth(today);
        var nextMonthEnd = EndOfMonth(today.AddMonths(1));

        // ThisWeek can run past the end of the month (a week spanning two months), in which case
        // nothing is left for ThisMonth to claim that ThisWeek has not already taken. Starting
        // ThisMonth after whichever of Tomorrow or the week's end is later is what keeps that true
        // without ThisMonth and ThisWeek ever needing to agree on who owns a given day.
        var thisMonthStart = tomorrow > weekEnd ? tomorrow : weekEnd;

        var todayRows = new List<T>();
        var tomorrowRows = new List<T>();
        var thisWeekRows = new List<T>();
        var thisMonthRows = new List<T>();
        var nextMonthRows = new List<T>();

        // Sorting up front means each bucket is filled in order already, so no section needs a sort
        // of its own afterwards.
        foreach (var row in rows.OrderBy(instant))
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant(row), zone).DateTime);

            if (localDate == today)
            {
                todayRows.Add(row);
            }
            else if (localDate == tomorrow)
            {
                tomorrowRows.Add(row);
            }
            else if (localDate > tomorrow && localDate <= weekEnd)
            {
                thisWeekRows.Add(row);
            }
            else if (localDate > thisMonthStart && localDate <= thisMonthEnd)
            {
                thisMonthRows.Add(row);
            }
            else if (localDate > thisMonthEnd && localDate <= nextMonthEnd)
            {
                nextMonthRows.Add(row);
            }
            // Else: outside every section the view can display. Dropped, not misfiled.
        }

        return
        [
            new ReminderSection<T>(ReminderSectionKey.Today, "Today", todayRows),
            new ReminderSection<T>(ReminderSectionKey.Tomorrow, "Tomorrow", tomorrowRows),
            new ReminderSection<T>(ReminderSectionKey.ThisWeek, "This week", thisWeekRows),
            new ReminderSection<T>(ReminderSectionKey.ThisMonth, "This month", thisMonthRows),
            new ReminderSection<T>(ReminderSectionKey.NextMonth, "Next month", nextMonthRows)
        ];
    }

    // The last day of the week containing `date`, given which day the week starts on. Computed from
    // the gap back to the most recent `weekStart` day rather than from a fixed DayOfWeek — see the
    // class remarks on why weekStart has to stay a parameter.
    private static DateOnly EndOfWeek(DateOnly date, DayOfWeek weekStart)
    {
        var daysSinceWeekStart = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;
        return date.AddDays(6 - daysSinceWeekStart);
    }

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
