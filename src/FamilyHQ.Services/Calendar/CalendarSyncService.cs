using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarSyncService : ICalendarSyncService
{
    private readonly IGoogleCalendarClient _googleCalendarClient;
    private readonly ICalendarRepository _calendarRepository;
    private readonly ILogger<CalendarSyncService> _logger;

    public CalendarSyncService(
        IGoogleCalendarClient googleCalendarClient,
        ICalendarRepository calendarRepository,
        ILogger<CalendarSyncService> logger)
    {
        _googleCalendarClient = googleCalendarClient;
        _calendarRepository = calendarRepository;
        _logger = logger;
    }

    public async Task SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting full sync of all calendars from {Start} to {End}", startDate, endDate);

        var googleCalendars = (await _googleCalendarClient.GetCalendarsAsync(ct)).ToList();
        var localCalendars = await _calendarRepository.GetCalendarsAsync(ct);

        // Remove local calendars that no longer exist in the user's Google Calendar list
        var obsoleteCalendars = localCalendars
            .Where(local => !googleCalendars.Any(g => g.GoogleCalendarId == local.GoogleCalendarId))
            .ToList();

        foreach (var obsolete in obsoleteCalendars)
        {
            _logger.LogInformation("Removing obsolete calendar {CalendarId} ({Name})", obsolete.Id, obsolete.DisplayName);
            await _calendarRepository.RemoveCalendarAsync(obsolete.Id, ct);
        }

        if (obsoleteCalendars.Count > 0)
            await _calendarRepository.SaveChangesAsync(ct);

        foreach (var googleCal in googleCalendars)
        {
            var localCal = localCalendars.FirstOrDefault(c => c.GoogleCalendarId == googleCal.GoogleCalendarId);
            if (localCal == null)
            {
                await _calendarRepository.AddCalendarAsync(googleCal, ct);
                await _calendarRepository.SaveChangesAsync(ct);
                localCal = googleCal;
            }

            await SyncAsync(localCal.Id, startDate, endDate, ct);
        }

        _logger.LogInformation("Finished syncing all calendars.");
    }

    public async Task SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var calendar = await _calendarRepository.GetCalendarByIdAsync(calendarInfoId, ct);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar {CalendarId} not found in DB. Skipping sync.", calendarInfoId);
            return;
        }

        bool isNewSyncState = false;
        var syncState = await _calendarRepository.GetSyncStateAsync(calendarInfoId, ct);
        if (syncState == null)
        {
            syncState = new SyncState { CalendarInfoId = calendarInfoId };
            isNewSyncState = true;
        }

        string? currentSyncToken = syncState.SyncToken;
        bool isFullSync = string.IsNullOrEmpty(currentSyncToken);

        _logger.LogInformation("Syncing events for {CalendarName}. IsFullSync: {IsFullSync}", calendar.DisplayName, isFullSync);

        try
        {
            var (events, nextSyncToken) = await _googleCalendarClient.GetEventsAsync(
                calendar.GoogleCalendarId,
                isFullSync ? startDate : null,
                isFullSync ? endDate : null,
                currentSyncToken,
                ct);

            var existingEvents = await _calendarRepository.GetEventsAsync(calendarInfoId, startDate, endDate, ct);

            if (isFullSync)
            {
                var fetchedGoogleEventIds = events.Select(e => e.GoogleEventId).ToHashSet();
                var obsoleteEvents = existingEvents.Where(e => !fetchedGoogleEventIds.Contains(e.GoogleEventId));
                foreach (var obsolete in obsoleteEvents)
                {
                    var tracked = await _calendarRepository.GetEventByGoogleEventIdAsync(obsolete.GoogleEventId, ct);
                    if (tracked == null) continue;

                    var calToRemove = tracked.Members.FirstOrDefault(c => c.Id == calendarInfoId);
                    if (calToRemove != null) tracked.Members.Remove(calToRemove);

                    if (!tracked.Members.Any())
                        await _calendarRepository.DeleteEventAsync(tracked.Id, ct);
                    else
                        await _calendarRepository.UpdateEventAsync(tracked, ct);
                }
            }

            foreach (var evt in events)
            {
                if (evt.Title == "CANCELLED_TOMBSTONE")
                {
                    var tracked = await _calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                    if (tracked != null)
                    {
                        var calToRemove = tracked.Members.FirstOrDefault(c => c.Id == calendarInfoId);
                        if (calToRemove != null) tracked.Members.Remove(calToRemove);

                        if (!tracked.Members.Any())
                            await _calendarRepository.DeleteEventAsync(tracked.Id, ct);
                        else
                            await _calendarRepository.UpdateEventAsync(tracked, ct);
                    }
                    continue;
                }

                var existingLinked = existingEvents.FirstOrDefault(e => e.GoogleEventId == evt.GoogleEventId);
                if (existingLinked != null)
                {
                    // Already linked to this calendar — update properties only.
                    // IsExternallyOwned is informational and set at initial insert; not refreshed here.
                    existingLinked.Title = evt.Title;
                    existingLinked.Start = evt.Start;
                    existingLinked.End = evt.End;
                    existingLinked.IsAllDay = evt.IsAllDay;
                    existingLinked.Location = evt.Location;
                    existingLinked.Description = evt.Description;
                    await _calendarRepository.UpdateEventAsync(existingLinked, ct);
                }
                else
                {
                    // Not yet linked to this calendar — check if it exists in any other calendar
                    var existingInDb = await _calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                    if (existingInDb != null)
                    {
                        // Get a tracked instance to ensure navigation property changes are persisted
                        var tracked = await _calendarRepository.GetEventByIdAsync(existingInDb.Id, ct);
                        if (tracked != null)
                        {
                            // Update properties and add this calendar link.
                            // OwnerCalendarInfoId is not changed — owner was set when event was first inserted.
                            // IsExternallyOwned is informational and set at initial insert; not refreshed here.
                            tracked.Title = evt.Title;
                            tracked.Start = evt.Start;
                            tracked.End = evt.End;
                            tracked.IsAllDay = evt.IsAllDay;
                            tracked.Location = evt.Location;
                            tracked.Description = evt.Description;
                            tracked.Members.Add(calendar);
                            await _calendarRepository.UpdateEventAsync(tracked, ct);
                        }
                    }
                    else
                    {
                        // Brand new event
                        evt.OwnerCalendarInfoId = calendar.Id;
                        evt.Members.Add(calendar);
                        await _calendarRepository.AddEventAsync(evt, ct);
                    }
                }
            }

            syncState.SyncToken = nextSyncToken;
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd = endDate;
            }

            if (isNewSyncState)
                await _calendarRepository.AddSyncStateAsync(syncState, ct);
            else
                await _calendarRepository.SaveSyncStateAsync(syncState, ct);

            await _calendarRepository.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully synced {Count} events for {CalendarName}.", events.Count(), calendar.DisplayName);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer valid"))
        {
            _logger.LogWarning("Sync token expired for {CalendarName}. Clearing and restarting full sync.", calendar.DisplayName);

            syncState.SyncToken = null;
            if (isNewSyncState)
                await _calendarRepository.AddSyncStateAsync(syncState, ct);
            else
                await _calendarRepository.SaveSyncStateAsync(syncState, ct);

            await _calendarRepository.SaveChangesAsync(ct);
            await SyncAsync(calendarInfoId, startDate, endDate, ct);
        }
    }
}
