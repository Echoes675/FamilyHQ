using FamilyHQ.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Re-evaluates event placement for every event in the current user's window when a calendar's
/// shared designation changes. Placement normally runs only on app edits; a designation change can
/// strand existing events on a stale calendar, so this sweep delegates each event/series to the
/// migration service to move it onto the correct calendar. Per-event/series failures are logged and
/// skipped so one bad row never aborts the whole pass (FHQ-47 Gap 1).
/// </summary>
public class PlacementReconciler(
    ICalendarRepository calendarRepository,
    ICalendarMigrationService migrationService,
    ILogger<PlacementReconciler> logger) : IPlacementReconciler
{
    public async Task<bool> ReconcileForUserAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var events = await calendarRepository.GetEventsAsync(start, end, ct);
        var changed = false;

        foreach (var e in events.Where(e => !e.IsRecurring))
        {
            try
            {
                // Re-load the event TRACKED before migrating (the enumeration above is AsNoTracking).
                // CalendarMigrationService.EnsureCorrectCalendarAsync calls UpdateEventAsync, which only
                // diffs the EventMembers junction correctly for a tracked entity; a detached one re-INSERTs
                // the already-persisted junction rows (duplicate-key). FHQ-68.
                var tracked = await calendarRepository.GetEventAsync(e.Id, ct);
                if (tracked is not null)
                    changed |= await migrationService.EnsureCorrectCalendarAsync(tracked, tracked.Members.ToList(), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Placement reconcile failed for event {EventId}; continuing.", e.Id);
            }
        }

        var series = events
            .Where(e => e.IsRecurring)
            .GroupBy(e => e.GoogleRecurringEventId!);

        foreach (var group in series)
        {
            var seriesId = group.Key;
            try
            {
                changed |= await migrationService.EnsureCorrectCalendarForSeriesAsync(seriesId, group.First().Members.ToList(), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Placement reconcile failed for series {SeriesId}; continuing.", seriesId);
            }
        }

        return changed;
    }
}
