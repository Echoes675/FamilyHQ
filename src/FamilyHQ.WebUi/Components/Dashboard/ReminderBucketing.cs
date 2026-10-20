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
    /// returns both, empty or not. Placement does not depend on it (<c>app.css</c> gives each panel
    /// an explicit <c>grid-column</c>/<c>grid-row</c> keyed on its slug, so a missing panel leaves
    /// the other where it is), but two readers do: <see cref="RemindersViewLogic.IsEntirelyEmpty"/>
    /// can only tell "nothing at all" from "nothing in one month" if both panels are present to be
    /// asked, and the per-panel "Nothing" line needs a panel to render it against. Collapsing an
    /// empty panel is the view's job; omitting it would be this class deciding the view's layout for
    /// it, with the two states above collapsed into one on the way.
    /// </summary>
    /// <remarks>
    /// "This month" runs from <paramref name="today"/> inclusive to the end of its month, and "Next
    /// month" from the day after to the end of the following month. Including today is the point: a
    /// panel whose near edge was tomorrow is a panel that cannot hold the rows a family reads first.
    /// <para>
    /// A row whose local day satisfies neither panel is dropped rather than forced into the nearer
    /// one, where it would quietly misrepresent the month it names. That catches a row beyond the
    /// window the server returned, and also a row BEFORE <paramref name="today"/>, which is not the
    /// impossibility it looks like. The server opens its window at local midnight in the zone
    /// FamilyHQ has STORED for the family (UTC if it has none it can resolve); this method is handed
    /// a <paramref name="today"/> and a <paramref name="zone"/> read off the KIOSK's own clock. The
    /// two agree whenever the stored zone was auto-detected, because the kiosk's reported zone is
    /// what gets stored — but a zone chosen explicitly in settings, or derived from a saved location,
    /// outranks whatever the kiosk reports, and then they are simply two different zones. A kiosk
    /// behind the stored zone is a kiosk whose window opened before its own midnight: the last hours
    /// of the kiosk's yesterday are inside it, so a past row really can arrive.
    /// </para>
    /// <para>
    /// Dropping it is still right, and would be right even if it could not happen: a past row shown
    /// among upcoming ones is worse than a missing one, because the family would act on it.
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
