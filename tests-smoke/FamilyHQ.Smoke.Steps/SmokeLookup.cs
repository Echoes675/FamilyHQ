using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The two questions nearly every smoke assertion starts with: what does Google hold for this scenario,
/// and what does preprod serve for it?
/// <para>
/// Both answers are scoped by the scenario's correlation marker or short id rather than by title text
/// alone. Smoke events are kept after a run, so the calendars carry every previous run's events too, and a
/// title match on its own would happily find last week's.
/// </para>
/// </summary>
public static class SmokeLookup
{
    /// <summary>
    /// How far either side of an event's date Google is searched. Wide enough to cover a bounded weekly
    /// series and a scenario that runs either side of midnight; narrow enough that the listing stays one
    /// page per calendar.
    /// </summary>
    private const int WindowDaysBefore = 2;

    /// <summary>
    /// The default reach forward. Wide enough for a bounded weekly or daily series; a yearly series asks
    /// for more, because its second occurrence is a whole year past its first.
    /// </summary>
    public const int DefaultWindowDaysAfter = 90;

    /// <summary>
    /// The reach forward a yearly series needs: a little over a year, so a two-occurrence yearly rule is
    /// fully inside the window whichever date it starts on.
    /// </summary>
    public const int YearlyWindowDaysAfter = 400;

    /// <summary>
    /// Every event carrying this scenario's marker, across every calendar the smoke account can be pushed
    /// for, paired with the calendar it is on.
    /// <para>
    /// Searching all of them — not just the one expected — is the point. "Written once, to the shared
    /// calendar only" cannot be checked by looking only at the shared calendar; a duplicate on a member
    /// calendar is exactly the failure mode worth catching.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<SmokeGoogleLocation>> FindInGoogleAsync(
        SmokeScenarioState state, DateOnly anchorDate, bool expandInstances = false,
        int daysAfter = DefaultWindowDaysAfter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var (from, to) = WindowAround(anchorDate, daysAfter);
        var found = new List<SmokeGoogleLocation>();

        foreach (var calendarName in state.Environment.Configuration.PushCapableCalendarNames)
        {
            var calendarId = state.Environment.Calendars.RequireGoogleId(calendarName);
            var events = await state.Environment.Google.FindEventsAsync(
                calendarId, state.Correlation.DescriptionMarker, from, to, expandInstances, ct);

            found.AddRange(events.Select(item => new SmokeGoogleLocation(calendarName, item)));
        }

        return found;
    }

    /// <summary>
    /// Waits, once and boundedly, until Google's view of this scenario satisfies
    /// <paramref name="settled"/>, then returns it.
    /// </summary>
    public static Task<IReadOnlyList<SmokeGoogleLocation>> WaitForGoogleAsync(
        SmokeScenarioState state,
        DateOnly anchorDate,
        Func<IReadOnlyList<SmokeGoogleLocation>, bool> settled,
        string failureDescription,
        bool expandInstances = false,
        int daysAfter = DefaultWindowDaysAfter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settled);

        return BoundedWait.ForAsync(
            async () =>
            {
                var found = await FindInGoogleAsync(state, anchorDate, expandInstances, daysAfter, ct);
                return settled(found) ? found : null;
            },
            failureDescription,
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));
    }

    /// <summary>
    /// Every event preprod serves that carries this scenario's short id in its title.
    /// <para>
    /// Keyed on the title's short id rather than on the description marker: preprod strips the managed
    /// <c>[members: …]</c> tag out of the description it serves, so description handling is preprod's
    /// business and not a safe join key, whereas the short id in the title is there precisely so a
    /// kiosk-side match cannot hit an earlier run (FHQ-141 principle 4).
    /// </para>
    /// </summary>
    public static Task<IReadOnlyList<PreprodEvent>> FindInPreprodAsync(
        SmokeScenarioState state, DateOnly anchorDate, CancellationToken ct = default) =>
        FindInPreprodAsync(state, MonthsAround(anchorDate), ct);

    /// <summary>
    /// The same, for a series whose occurrences are too far apart to sit inside one run of consecutive
    /// months — a yearly rule, principally. The caller names the months, deriving them from Google's own
    /// expansion rather than from arithmetic of its own.
    /// </summary>
    public static async Task<IReadOnlyList<PreprodEvent>> FindInPreprodAsync(
        SmokeScenarioState state, IReadOnlyList<DateOnly> months, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var events = await state.RequireApi().GetEventsAsync(months, ct);
        return [.. events.Where(item => state.Correlation.TitleCarriesShortId(item.Title))];
    }

    /// <summary>Waits, once and boundedly, until preprod's view of this scenario satisfies <paramref name="settled"/>.</summary>
    public static Task<IReadOnlyList<PreprodEvent>> WaitForPreprodAsync(
        SmokeScenarioState state,
        DateOnly anchorDate,
        Func<IReadOnlyList<PreprodEvent>, bool> settled,
        string failureDescription,
        CancellationToken ct = default) =>
        WaitForPreprodAsync(state, MonthsAround(anchorDate), settled, failureDescription, ct);

    /// <summary>Waits, once and boundedly, until preprod's view across <paramref name="months"/> satisfies <paramref name="settled"/>.</summary>
    public static Task<IReadOnlyList<PreprodEvent>> WaitForPreprodAsync(
        SmokeScenarioState state,
        IReadOnlyList<DateOnly> months,
        Func<IReadOnlyList<PreprodEvent>, bool> settled,
        string failureDescription,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settled);

        return BoundedWait.ForAsync(
            async () =>
            {
                var found = await FindInPreprodAsync(state, months, ct);
                return settled(found) ? found : null;
            },
            failureDescription,
            TimeSpan.FromSeconds(state.Environment.Configuration.PushWaitSeconds));
    }

    /// <summary>The Google search window around <paramref name="anchorDate"/>, as absolute instants.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) WindowAround(
        DateOnly anchorDate, int daysAfter = DefaultWindowDaysAfter)
    {
        var from = FamilyClock.ToFamilyOffset(anchorDate.AddDays(-WindowDaysBefore).ToDateTime(TimeOnly.MinValue));
        var to = FamilyClock.ToFamilyOffset(anchorDate.AddDays(daysAfter).ToDateTime(TimeOnly.MinValue));
        return (from, to);
    }

    /// <summary>
    /// The months preprod has to be asked about to see everything in the window. A bounded weekly series
    /// can start in one month and finish in the next, and the month view only answers for one month at a
    /// time.
    /// </summary>
    public static IReadOnlyList<DateOnly> MonthsAround(DateOnly anchorDate)
    {
        var first = new DateOnly(anchorDate.Year, anchorDate.Month, 1);
        return [first.AddMonths(-1), first, first.AddMonths(1), first.AddMonths(2)];
    }

    /// <summary>
    /// The months preprod has to be asked about to see <paramref name="dates"/> — each date's month plus
    /// the month either side of it.
    /// <para>
    /// The neighbouring months are what keep this an assertion rather than a leading question. Asking only
    /// about the months Google's own instances fall in would find the expected occurrences and stay blind
    /// to an extra one a few days off, which is exactly the failure worth catching when two expansions
    /// disagree.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DateOnly> MonthsCovering(IEnumerable<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);

        var months = new SortedSet<DateOnly>();
        foreach (var date in dates)
        {
            var first = new DateOnly(date.Year, date.Month, 1);
            months.Add(first.AddMonths(-1));
            months.Add(first);
            months.Add(first.AddMonths(1));
        }

        return [.. months];
    }
}
