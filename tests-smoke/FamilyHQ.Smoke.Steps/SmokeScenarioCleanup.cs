namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// How a scenario that passed takes its own events off the family's calendars again.
/// <para>
/// Only a scenario that <b>passed</b>: see
/// <see cref="Hooks.SmokeScenarioHooks.RemoveThisScenariosEventsOnSuccessAsync"/> for why a failed one keeps
/// everything, and for why this is not the "compensation" the suite's principles forbid — every assertion
/// the scenario makes has already been made and answered by the time anything here runs.
/// </para>
/// </summary>
public static class SmokeScenarioCleanup
{
    /// <summary>
    /// Deletes the events carrying this scenario's correlation marker from Google, and reports what it did
    /// through <paramref name="report"/>.
    /// <para>
    /// Scoped to the marker and nothing else. Not the day, not the calendar, not a title pattern: a sweep of
    /// a day would take the events a *failed* scenario deliberately left behind, and a sweep of a calendar
    /// would take a real event off a live account. The marker is written into every description this suite
    /// creates, and it belongs to one scenario of one run.
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
            var found = await SmokeLookup.FindInGoogleAsync(
                state, anchor, expandInstances: false, state.WindowDaysAfter);

            if (found.Count == 0)
            {
                return;
            }

            // Masters first. Deleting a master cancels its occurrences and the exceptions singled out of it,
            // so the rest of the list is usually already gone by the time it is reached — which the oracle's
            // delete treats as done rather than as an error. Ordering this way just keeps the traffic down;
            // a "this and following" scenario leaves two masters, and both are in this list.
            foreach (var location in found.OrderByDescending(
                         candidate => SmokeSeries.IsRecurrenceMaster(candidate.Event)))
            {
                var calendarId = state.Environment.Calendars.RequireGoogleId(location.CalendarName);
                await state.Environment.Google.DeleteEventAsync(calendarId, location.Event.Id);
            }

            report($"  cleaned up {found.Count} event(s) this scenario created, via Google.");

            var left = await SmokeLookup.FindInGoogleAsync(
                state, anchor, expandInstances: false, state.WindowDaysAfter);

            if (left.Count > 0)
            {
                report(
                    $"  NOTE: {left.Count} event(s) carrying correlation {state.Correlation.Id} are still in "
                    + "Google after cleanup. They will sit on the days this scenario used and may crowd out a "
                    + "later run. Remove them by hand, and see the preprod smoke maintenance guide.");
            }
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
    /// The date to search around: where the scenario put its events, falling back to the block it reserved.
    /// Null when it did neither, which means it created nothing.
    /// </summary>
    private static DateOnly? AnchorOf(SmokeScenarioState state) =>
        state.EventDate != default ? state.EventDate : state.ReservedDays?.FirstDay;
}
