namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Files a flat list of rows into the two panels the reminders timeline lays out side by side, by one
/// instant read off each row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two calendar months and not a near/far mix.</b> This filed into five sections — Today,
/// Tomorrow, This week, This month, Next month — and two of them were structurally empty rather than
/// merely often empty: on a Saturday the week's end IS tomorrow, so no day was left for This week to
/// claim, and once the current week ran past the month's end the same was true of This month. Over a
/// year that is 104 days with an empty This week and 51 with an empty This month. Two calendar-month
/// panels can each always claim at least one day, and the days a family cares about most are named on
/// the rows themselves (<see cref="ReminderRowDisplay.EventTime"/>) rather than by the panel holding
/// them — which is what makes a single panel per month enough.
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
    /// Files <paramref name="rows"/> into the two panels — This month then Next month — and always
    /// returns both: an absent panel would move the other one into its column of the view's fixed
    /// two-column layout, and collapsing an empty one is the view's job, not this class's.
    /// </summary>
    /// <remarks>
    /// "This month" runs from <paramref name="today"/> inclusive to the end of its month, and "Next
    /// month" from the day after to the end of the following month. Including today is the point: a
    /// panel whose near edge was tomorrow is a panel that cannot hold the rows a family reads first.
    /// <para>
    /// A row whose local day satisfies neither panel is dropped rather than forced into the nearer
    /// one, where it would quietly misrepresent the month it names. That catches a row beyond the
    /// window the server returned, and also a row BEFORE <paramref name="today"/> — the server's
    /// window opens at local midnight so one should never arrive, which is exactly why it must not be
    /// swept into "This month" if it ever does. A past row shown among upcoming ones is worse than a
    /// missing one: the family would act on it.
    /// </para>
    /// </remarks>
    /// <param name="rows">The rows to file. Never mutated or reordered in place.</param>
    /// <param name="instant">
    /// Reads the instant a row is filed and sorted by. A <c>Func</c> rather than a view-model
    /// dependency, so this class needs nothing about the row beyond the one value it files by.
    /// </param>
    /// <param name="today">The kiosk's current local date.</param>
    /// <param name="zone">
    /// The zone a row's local day is read in. A row's day is its day <i>in this zone</i>, not its
    /// UTC day — the two disagree for several hours around every midnight, and for an extra hour
    /// either side of a daylight-saving transition.
    /// </param>
    public static IReadOnlyList<ReminderSection<T>> File<T>(
        IEnumerable<T> rows,
        Func<T, DateTimeOffset> instant,
        DateOnly today,
        TimeZoneInfo zone)
    {
        var thisMonthEnd = EndOfMonth(today);
        var nextMonthEnd = EndOfMonth(today.AddMonths(1));

        var thisMonthRows = new List<T>();
        var nextMonthRows = new List<T>();

        // Sorting up front means each panel is filled in order already, so no panel needs a sort of
        // its own afterwards.
        foreach (var row in rows.OrderBy(instant))
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant(row), zone).DateTime);

            if (localDate >= today && localDate <= thisMonthEnd)
            {
                thisMonthRows.Add(row);
            }
            else if (localDate > thisMonthEnd && localDate <= nextMonthEnd)
            {
                nextMonthRows.Add(row);
            }
            // Else: outside both panels the view can display. Dropped, not misfiled.
        }

        return
        [
            new ReminderSection<T>(ReminderSectionKey.ThisMonth, "This month", thisMonthRows),
            new ReminderSection<T>(ReminderSectionKey.NextMonth, "Next month", nextMonthRows)
        ];
    }

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
