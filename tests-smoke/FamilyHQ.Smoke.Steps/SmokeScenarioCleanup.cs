using FamilyHQ.Smoke.Data.Models;
using FamilyHQ.Smoke.Steps.Preflight;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// How a scenario that passed takes its own events off the family's calendars again.
/// <para>
/// Only a scenario that <b>passed</b>: see
/// <see cref="Hooks.SmokeScenarioHooks.RemoveThisScenariosEventsOnSuccessAsync"/> for why a failed one keeps
/// everything, and for why this is not the "compensation" the suite's principles forbid — every assertion
/// the scenario makes has already been made and answered by the time anything here runs.
/// </para>
/// <para>
/// <b>Cheap on purpose.</b> Google pauses push delivery to this account for minutes at a time when it has
/// been worked hard, and the pauses are what make scenarios time out waiting for a change to arrive. Cleanup
/// is pure overhead against that budget — it proves nothing — so it asks Google for as little as it can:
/// one listing, of the calendars this scenario actually touched, and no confirmation pass. The confirmation
/// happens once for the whole run instead (<see cref="ReportAnythingLeftBehindAsync"/>).
/// </para>
/// </summary>
public static class SmokeScenarioCleanup
{
    private static int _scenariosThatKeptTheirEvents;

    private static readonly HashSet<string> MarkersUsedThisRun = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a scenario's marker, so the end-of-run check can tell <i>this</i> run's leftovers from the
    /// evidence an earlier failed run deliberately left behind.
    /// </summary>
    public static void NoteScenarioStarted(string descriptionMarker)
    {
        lock (MarkersUsedThisRun)
        {
            MarkersUsedThisRun.Add(descriptionMarker);
        }
    }

    /// <summary>Records that a scenario failed, so the end-of-run check knows leftovers are expected.</summary>
    public static void NoteEventsKeptAfterFailure() =>
        Interlocked.Increment(ref _scenariosThatKeptTheirEvents);

    /// <summary>
    /// Deletes the events carrying this scenario's correlation marker from Google, and reports what it did
    /// through <paramref name="report"/>.
    /// <para>
    /// Scoped to the marker and nothing else. Not the day, not a title pattern: a sweep of a day would take
    /// the events a *failed* scenario deliberately left behind, and a blanket sweep would take a real event
    /// off a live account. The marker is written into every description this suite creates, and it belongs
    /// to one scenario of one run.
    /// </para>
    /// </summary>
    public static async Task RemoveEventsAsync(SmokeScenarioState state, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);

        // A scenario that created nothing — preflight, weather — has no anchor and nothing to remove. So has
        // a run that never obtained an oracle, in which case no scenario got as far as creating an event.
        if (state.Environment.OracleOrNull is null || AnchorOf(state) is not { } anchor)
        {
            return;
        }

        try
        {
            var found = await FindOnTouchedCalendarsAsync(state, anchor);
            if (found.Count == 0)
            {
                return;
            }

            // Masters first. Deleting a master cancels its occurrences and the exceptions singled out of it,
            // so the rest of the list is usually already gone by the time it is reached — which the oracle's
            // delete treats as done rather than as an error. A "this and following" scenario leaves two
            // masters, and both are in this list.
            foreach (var location in found.OrderByDescending(
                         candidate => SmokeSeries.IsRecurrenceMaster(candidate.Event)))
            {
                var calendarId = state.Environment.Calendars.RequireGoogleId(location.CalendarName);
                await state.Environment.Google.DeleteEventAsync(calendarId, location.Event.Id);
            }

            report($"  cleaned up {found.Count} event(s) this scenario created, via Google.");
        }
        catch (Exception ex)
        {
            // A cleanup problem must never turn a scenario that passed into one that failed: what the
            // scenario set out to prove, it has already proved. It must not be silent either — events left
            // behind are what crowds a later run out — so it is reported and the run carries on.
            report(
                $"  cleanup could not remove this scenario's events ({ex.GetType().Name}: {ex.Message}). "
                + $"They are still on the calendars under correlation {state.Correlation.Id}.");
        }
    }

    /// <summary>
    /// Once per run, and only when every scenario passed: checks that the suite left nothing behind.
    /// <para>
    /// This is the safety net for the narrow listing above. Cleanup looks only at the calendars a scenario
    /// meant to use, so an event written to a calendar nobody expected would survive it — and that is
    /// precisely the bug worth catching, because it is how the calendars silently filled up before. Doing the
    /// check once for the whole run costs one listing per calendar instead of one per scenario.
    /// </para>
    /// <para>
    /// Skipped entirely when any scenario failed, because a failed scenario keeps its events on purpose and
    /// reporting them as leftovers would bury the real failure under a false one.
    /// </para>
    /// </summary>
    public static async Task ReportAnythingLeftBehindAsync(
        SmokeRunEnvironment environment, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(report);

        if (environment.OracleOrNull is null || Volatile.Read(ref _scenariosThatKeptTheirEvents) > 0)
        {
            return;
        }

        string[] markers;
        lock (MarkersUsedThisRun)
        {
            markers = [.. MarkersUsedThisRun];
        }

        if (markers.Length == 0)
        {
            return;
        }

        try
        {
            var leftovers = 0;
            foreach (var calendarName in environment.Configuration.PushCapableCalendarNames)
            {
                var calendarId = environment.Calendars.RequireGoogleId(calendarName);
                var (from, to) = SmokeLookup.WindowAround(
                    FamilyHQ.Smoke.Common.Helpers.FamilyClock.Today, SmokeLookup.YearlyWindowDaysAfter);

                // The suite's shared marker narrows the listing; this run's own markers decide what
                // counts. Without that second step the check reports the events a *previous* run's failure
                // kept on purpose, and a warning that cries wolf is one nobody reads - which is how the
                // calendars filled up unnoticed in the first place.
                var events = await environment.Google.FindEventsAsync(
                    calendarId, SmokeMarker, from, to, expandInstances: false);

                leftovers += events.Count(
                    candidate => markers.Any(
                        marker => (candidate.Description ?? string.Empty)
                            .Contains(marker, StringComparison.Ordinal)));
            }

            if (leftovers > 0)
            {
                report(
                    $"LEFTOVERS: {leftovers} event(s) from THIS run are still on the calendars although "
                    + "every scenario passed and cleaned up after itself. Something was written to a "
                    + "calendar the scenario that made it did not expect to touch, so its own cleanup did "
                    + "not look there. Left alone rather than swept - see the preprod smoke maintenance "
                    + "guide.");
            }
        }
        catch (Exception ex)
        {
            report($"  end-of-run leftover check could not run ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>The marker every description this suite writes carries, without a scenario's id appended.</summary>
    private const string SmokeMarker = "smoke-correlation:";

    /// <summary>
    /// This scenario's events, looked for only on the calendars it had reason to touch.
    /// <para>
    /// The wide scan that <see cref="SmokeLookup.FindInGoogleAsync"/> does — every push-capable calendar —
    /// is right for an <i>assertion</i>, because "written once, to the shared calendar only" cannot be
    /// checked by looking at one calendar. Cleanup has no such question to answer and pays five calls for it,
    /// so it looks where the scenario worked and lets the end-of-run check cover the rest.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<SmokeGoogleLocation>> FindOnTouchedCalendarsAsync(
        SmokeScenarioState state, DateOnly anchor)
    {
        var (from, to) = SmokeLookup.WindowAround(anchor, state.WindowDaysAfter);
        var found = new List<SmokeGoogleLocation>();

        foreach (var calendarName in TouchedCalendars(state))
        {
            var calendarId = state.Environment.Calendars.RequireGoogleId(calendarName);
            var events = await state.Environment.Google.FindEventsAsync(
                calendarId, state.Correlation.DescriptionMarker, from, to, expandInstances: false);

            found.AddRange(events.Select(item => new SmokeGoogleLocation(calendarName, item)));
        }

        return found;
    }

    /// <summary>
    /// The calendars a scenario can have written to.
    /// <para>
    /// The shared calendar is included whenever the scenario named more than one member, because that is
    /// where a multi-member event goes — the members are named on it, but it does not live on their own
    /// calendars. Missing that would leave every multi-member event behind.
    /// </para>
    /// </summary>
    private static IEnumerable<string> TouchedCalendars(SmokeScenarioState state)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (state.SeededCalendarName is { } seeded)
        {
            names.Add(seeded);
        }

        foreach (var master in state.SeriesMasters)
        {
            names.Add(master.CalendarName);
        }

        foreach (var member in state.MemberNames)
        {
            names.Add(member);
        }

        if (state.MemberNames.Count > 1)
        {
            names.Add(state.Environment.Configuration.SharedCalendar);
        }

        return names;
    }

    /// <summary>
    /// The date to search around: where the scenario put its events, falling back to the block it reserved.
    /// Null when it did neither, which means it created nothing.
    /// </summary>
    private static DateOnly? AnchorOf(SmokeScenarioState state) =>
        state.EventDate != default ? state.EventDate : state.ReservedDays?.FirstDay;
}
