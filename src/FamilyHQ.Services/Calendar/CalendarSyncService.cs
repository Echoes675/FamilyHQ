using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarSyncService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    IMemberTagParser memberTagParser,
    ILogger<CalendarSyncService> logger) : ICalendarSyncService
{
    public async Task SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        logger.LogInformation("Starting full sync from {Start} to {End}", startDate, endDate);

        var googleCalendars = (await googleCalendarClient.GetCalendarsAsync(ct)).ToList();
        var localCalendars  = await calendarRepository.GetCalendarsAsync(ct);

        // Remove obsolete local calendars
        var obsolete = localCalendars
            .Where(local => !googleCalendars.Any(g => g.GoogleCalendarId == local.GoogleCalendarId))
            .ToList();

        foreach (var cal in obsolete)
        {
            logger.LogInformation("Removing obsolete calendar {CalendarId}", cal.Id);
            await calendarRepository.RemoveCalendarAsync(cal.Id, ct);
        }

        if (obsolete.Count > 0)
            await calendarRepository.SaveChangesAsync(ct);

        foreach (var googleCal in googleCalendars)
        {
            var localCal = localCalendars.FirstOrDefault(c => c.GoogleCalendarId == googleCal.GoogleCalendarId);
            if (localCal == null)
            {
                await calendarRepository.AddCalendarAsync(googleCal, ct);
                await calendarRepository.SaveChangesAsync(ct);
                localCal = googleCal;
            }

            await SyncAsync(localCal.Id, startDate, endDate, ct);
        }

        logger.LogInformation("Finished syncing all calendars.");
    }

    public Task SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
        => SyncCoreAsync(calendarInfoId, startDate, endDate, isRetry: false, ct);

    private async Task SyncCoreAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, bool isRetry, CancellationToken ct)
    {
        var calendar = await calendarRepository.GetCalendarByIdAsync(calendarInfoId, ct);
        if (calendar == null)
        {
            logger.LogWarning("Calendar {CalendarId} not found. Skipping sync.", calendarInfoId);
            return;
        }

        bool isNewSyncState = false;
        var syncState = await calendarRepository.GetSyncStateAsync(calendarInfoId, ct);
        if (syncState == null)
        {
            syncState = new SyncState { CalendarInfoId = calendarInfoId };
            isNewSyncState = true;
        }

        bool isFullSync = string.IsNullOrEmpty(syncState.SyncToken);

        logger.LogInformation("Syncing {CalendarName}. FullSync={IsFullSync}", calendar.DisplayName, isFullSync);

        try
        {
            var (events, nextSyncToken) = await googleCalendarClient.GetEventsAsync(
                calendar.GoogleCalendarId,
                isFullSync ? startDate : null,
                isFullSync ? endDate : null,
                syncState.SyncToken,
                ct);

            var allLocalCalendars = await calendarRepository.GetCalendarsAsync(ct);
            var knownMemberNames  = allLocalCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
            var calendarByName    = allLocalCalendars.Where(c => !c.IsShared)
                .ToDictionary(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

            if (isFullSync)
            {
                // Tombstone events no longer present in Google
                var existingEvents   = await calendarRepository.GetEventsByOwnerCalendarAsync(calendarInfoId, startDate, endDate, ct);
                var fetchedGoogleIds = events.Select(e => e.GoogleEventId).ToHashSet();
                var obsoleteEvents   = existingEvents.Where(e => !fetchedGoogleIds.Contains(e.GoogleEventId));

                foreach (var obsoleteEvt in obsoleteEvents)
                    await calendarRepository.DeleteEventAsync(obsoleteEvt.Id, ct);
            }

            foreach (var evt in events)
            {
                if (evt.Title == "CANCELLED_TOMBSTONE")
                {
                    var tracked = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                    if (tracked != null)
                        await calendarRepository.DeleteEventAsync(tracked.Id, ct);
                    continue;
                }

                // Derive members from description
                var parsedNames   = memberTagParser.ParseMembers(evt.Description, knownMemberNames);
                var parsedMembers = parsedNames
                    .Where(n => calendarByName.ContainsKey(n))
                    .Select(n => calendarByName[n])
                    .ToList();

                // If no members parsed and this is an individual calendar, default to owning calendar's member
                if (parsedMembers.Count == 0 && !calendar.IsShared)
                    parsedMembers.Add(calendar);

                var existing = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                if (existing != null)
                {
                    existing.Title       = evt.Title;
                    existing.Start       = evt.Start;
                    existing.End         = evt.End;
                    existing.IsAllDay    = evt.IsAllDay;
                    existing.Location    = evt.Location;
                    existing.Description = evt.Description;
                    existing.Members     = parsedMembers;
                    await calendarRepository.UpdateEventAsync(existing, ct);
                }
                else
                {
                    evt.OwnerCalendarInfoId = calendar.Id;
                    evt.Members             = parsedMembers;
                    await calendarRepository.AddEventAsync(evt, ct);
                }
            }

            syncState.SyncToken    = nextSyncToken;
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd   = endDate;
            }

            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);

            await calendarRepository.SaveChangesAsync(ct);
            logger.LogInformation("Synced {Count} events for {CalendarName}.", events.Count(), calendar.DisplayName);
        }
        catch (InvalidOperationException ex) when (!isRetry && ex.Message.Contains("no longer valid"))
        {
            logger.LogWarning("Sync token expired for {CalendarName}. Restarting full sync.", calendar.DisplayName);
            syncState.SyncToken = null;
            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);
            await calendarRepository.SaveChangesAsync(ct);
            await SyncCoreAsync(calendarInfoId, startDate, endDate, isRetry: true, ct);
        }
    }
}
