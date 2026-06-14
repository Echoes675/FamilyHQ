using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarMigrationService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    IMemberTagParser memberTagParser,
    IOutboundWriteHashCache outboundCache,
    ILogger<CalendarMigrationService> logger) : ICalendarMigrationService
{
    public async Task<bool> EnsureCorrectCalendarAsync(
        CalendarEvent calendarEvent,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default)
    {
        var currentOwner = await calendarRepository.GetCalendarByIdAsync(calendarEvent.OwnerCalendarInfoId, ct)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found.");

        var shouldBeShared = assignedMembers.Count > 1;
        var isCurrentlyShared = currentOwner.IsShared;

        if (shouldBeShared == isCurrentlyShared)
            return false; // already on the correct calendar

        CalendarInfo targetCalendar;
        if (shouldBeShared)
        {
            var sharedCal = await calendarRepository.GetSharedCalendarAsync(ct)
                ?? throw new InvalidOperationException("No shared calendar is configured. Set IsShared = true on one calendar.");
            targetCalendar = sharedCal;
        }
        else
        {
            // assignedMembers must contain exactly the one non-shared individual member
            targetCalendar = assignedMembers.SingleOrDefault(m => !m.IsShared)
                ?? throw new InvalidOperationException(
                    $"Cannot migrate event to individual calendar: no non-shared member found in assignedMembers.");
        }

        logger.LogInformation(
            "Migrating event {GoogleEventId} from {Source} to {Target}.",
            calendarEvent.GoogleEventId, currentOwner.DisplayName, targetCalendar.DisplayName);

        // FHQ-68: stamp an explicit [members: ...] tag (non-shared member display names) so a
        // Google-originated event's free-form description does not re-resolve to different members on the
        // next sync from the target calendar (which would oscillate). Idempotent for already-tagged events.
        var memberNames = assignedMembers.Where(m => !m.IsShared).Select(m => m.DisplayName).ToList();
        calendarEvent.Description = memberTagParser.NormaliseDescription(
            memberTagParser.StripMemberTag(calendarEvent.Description), memberNames);

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        // Capture the old ID before CreateEventAsync, which mutates calendarEvent.GoogleEventId
        // to the new Google-assigned ID on the target calendar.
        var oldGoogleEventId = calendarEvent.GoogleEventId;

        var created = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        // Record the outbound write hash for the new Google event BEFORE any subsequent
        // steps so that the echo-guard can suppress the incoming webhook even if the
        // delete-old step later fails (the new event already exists in Google).
        outboundCache.Record(created.GoogleEventId, hash);
        logger.LogDebug(
            "Recorded outbound write hash for event {EventId} (hash {Hash}).",
            created.GoogleEventId, hash);

        // Persist the new DB state before deleting from Google.
        // If SaveChanges fails, both Google events still exist (recoverable).
        // If Delete fails after a successful save, the old Google event is orphaned
        // but the DB is consistent — the next sync will clean up the orphan.
        calendarEvent.GoogleEventId = created.GoogleEventId;
        calendarEvent.OwnerCalendarInfoId = targetCalendar.Id;

        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        await googleCalendarClient.DeleteEventAsync(currentOwner.GoogleCalendarId, oldGoogleEventId, ct);

        return true;
    }

    public async Task<bool> EnsureCorrectCalendarForSeriesAsync(
        string seriesId,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default)
    {
        var localInstances = await calendarRepository.GetEventsBySeriesIdAsync(seriesId, ct);
        if (localInstances.Count == 0)
            throw new InvalidOperationException($"No local instances found for series {seriesId}.");

        // A representative instance carries the series' current owner calendar, RRULE and content.
        var representative = localInstances[0];
        var currentOwner = await calendarRepository.GetCalendarByIdAsync(representative.OwnerCalendarInfoId, ct)
            ?? throw new InvalidOperationException($"Owner calendar {representative.OwnerCalendarInfoId} not found for series {seriesId}.");

        var shouldBeShared = assignedMembers.Count > 1;
        if (shouldBeShared == currentOwner.IsShared)
            return false; // series already on the correct calendar

        CalendarInfo targetCalendar;
        if (shouldBeShared)
        {
            targetCalendar = await calendarRepository.GetSharedCalendarAsync(ct)
                ?? throw new InvalidOperationException("No shared calendar is configured. Set IsShared = true on one calendar.");
        }
        else
        {
            targetCalendar = assignedMembers.SingleOrDefault(m => !m.IsShared)
                ?? throw new InvalidOperationException(
                    "Cannot migrate series to individual calendar: no non-shared member found in assignedMembers.");
        }

        var rrule = representative.RecurrenceRule
            ?? throw new InvalidOperationException($"Series {seriesId} has no stored RecurrenceRule to migrate.");

        logger.LogInformation(
            "Migrating series {SeriesId} from {Source} to {Target}.",
            seriesId, currentOwner.DisplayName, targetCalendar.DisplayName);

        // Insert the series on the target calendar with the new RRULE and a normalised members tag.
        var memberNames = assignedMembers.Where(m => !m.IsShared).Select(m => m.DisplayName).ToList();
        var description = memberTagParser.NormaliseDescription(
            memberTagParser.StripMemberTag(representative.Description), memberNames);

        var newMaster = new CalendarEvent
        {
            Title = representative.Title,
            Start = representative.Start,
            End = representative.End,
            IsAllDay = representative.IsAllDay,
            Location = representative.Location,
            Description = description
        };

        var masterHash = EventContentHash.Compute(
            newMaster.Title, newMaster.Start, newMaster.End, newMaster.IsAllDay, newMaster.Description);

        var created = await googleCalendarClient.CreateRecurringEventAsync(
            targetCalendar.GoogleCalendarId, newMaster, masterHash, rrule, ct);
        var newSeriesId = created.GoogleEventId;

        outboundCache.Record(newSeriesId, masterHash);
        logger.LogDebug("Recorded outbound write hash for new series master {SeriesId} (hash {Hash}).", newSeriesId, masterHash);

        // Remove the old series' local rows; the reconcile below materialises the new series' instances.
        foreach (var instance in localInstances)
            await calendarRepository.DeleteEventAsync(instance.Id, ct);
        await calendarRepository.SaveChangesAsync(ct);

        await ReconcileSeriesWindowAsync(targetCalendar, newSeriesId, rrule, ct);

        // Delete the old series from Google last; if it fails the DB is already consistent and the
        // next sync cleans up the orphaned old series.
        await googleCalendarClient.DeleteEventAsync(currentOwner.GoogleCalendarId, seriesId, ct);

        return true;
    }

    // Re-fetch the target calendar's sync window and persist the migrated series' expanded instances,
    // recording an outbound hash for each so the resulting webhooks are recognised as self-echoes.
    private async Task ReconcileSeriesWindowAsync(CalendarInfo target, string newSeriesId, string rrule, CancellationToken ct)
    {
        var syncState = await calendarRepository.GetSyncStateAsync(target.Id, ct);
        if (syncState?.SyncWindowStart is not { } windowStart || syncState.SyncWindowEnd is not { } windowEnd)
            throw new InvalidOperationException(
                $"Cannot reconcile migrated series: calendar {target.Id} has no stored sync window.");

        var (fetched, _) = await googleCalendarClient.GetEventsAsync(
            target.GoogleCalendarId, windowStart, windowEnd, null, ct);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        // FHQ-47 (Gap 2): mirror CalendarSyncService — the free-form fallback resolves against
        // non-shared calendars only, while an explicit "[members: ...]" tag is authoritative and
        // resolves against ALL calendars, so a tagged member is not dropped while its calendar is
        // transiently shared (the first-login auto-designation window).
        var knownMemberNames = allCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
        var allCalendarNames = allCalendars.Select(c => c.DisplayName).ToList();

        foreach (var fetchedEvent in fetched.Where(e => e.GoogleRecurringEventId == newSeriesId))
        {
            var parsedNames = memberTagParser.ParseMembers(fetchedEvent.Description, knownMemberNames, allCalendarNames);
            var members = allCalendars
                .Where(c => parsedNames.Contains(c.DisplayName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            fetchedEvent.OwnerCalendarInfoId = target.Id;
            fetchedEvent.RecurrenceRule = fetchedEvent.RecurrenceRule ?? rrule;
            fetchedEvent.Members = members;
            await calendarRepository.AddEventAsync(fetchedEvent, ct);

            // Record the hash Google will echo for this instance. Google copies the new master's
            // content-hash extended property onto every expanded instance, so we record that echoed
            // value (surfaced on ContentHash by GetEventsAsync) — a per-instance recompute would
            // never match IsSelfEcho and would let the N follow-on webhooks through as a loop.
            if (!string.IsNullOrEmpty(fetchedEvent.ContentHash))
            {
                outboundCache.Record(fetchedEvent.GoogleEventId, fetchedEvent.ContentHash);
                logger.LogDebug(
                    "Recorded outbound write hash for migrated series instance {EventId} (hash {Hash}).",
                    fetchedEvent.GoogleEventId, fetchedEvent.ContentHash);
            }
        }

        await calendarRepository.SaveChangesAsync(ct);
    }
}
