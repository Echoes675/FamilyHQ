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

    private const int WindowDaysAfter = 90;

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
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var (from, to) = WindowAround(anchorDate);
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
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settled);

        return BoundedWait.ForAsync(
            async () =>
            {
                var found = await FindInGoogleAsync(state, anchorDate, expandInstances, ct);
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
    public static async Task<IReadOnlyList<PreprodEvent>> FindInPreprodAsync(
        SmokeScenarioState state, DateOnly anchorDate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var events = await state.RequireApi().GetEventsAsync(MonthsAround(anchorDate), ct);
        return [.. events.Where(item => state.Correlation.TitleCarriesShortId(item.Title))];
    }

    /// <summary>Waits, once and boundedly, until preprod's view of this scenario satisfies <paramref name="settled"/>.</summary>
    public static Task<IReadOnlyList<PreprodEvent>> WaitForPreprodAsync(
        SmokeScenarioState state,
        DateOnly anchorDate,
        Func<IReadOnlyList<PreprodEvent>, bool> settled,
        string failureDescription,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settled);

        return BoundedWait.ForAsync(
            async () =>
            {
                var found = await FindInPreprodAsync(state, anchorDate, ct);
                return settled(found) ? found : null;
            },
            failureDescription,
            TimeSpan.FromSeconds(state.Environment.Configuration.PushWaitSeconds));
    }

    /// <summary>The Google search window around <paramref name="anchorDate"/>, as absolute instants.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) WindowAround(DateOnly anchorDate)
    {
        var from = FamilyClock.ToFamilyOffset(anchorDate.AddDays(-WindowDaysBefore).ToDateTime(TimeOnly.MinValue));
        var to = FamilyClock.ToFamilyOffset(anchorDate.AddDays(WindowDaysAfter).ToDateTime(TimeOnly.MinValue));
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
}
