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

        // 1. Fetch the user's calendars from Google
        var googleCalendars = await _googleCalendarClient.GetCalendarsAsync(ct);
        var localCalendars = await _calendarRepository.GetCalendarsAsync(ct);

        // 2. Add or update calendars in our DB
        foreach (var googleCal in googleCalendars)
        {
            var localCal = localCalendars.FirstOrDefault(c => c.GoogleCalendarId == googleCal.GoogleCalendarId);
            if (localCal == null)
            {
                await _calendarRepository.AddCalendarAsync(googleCal, ct);
                
                // Save changes immediately so we get a DB ID for the CalendarInfo
                await _calendarRepository.SaveChangesAsync(ct);
                localCal = googleCal;
            }
            else
            {
                // Update properties but let EF track it (since it's AsNoTracking in GetCalendarsAsync, we fetch properly later, but in a real app we'd attach or use tracked query)
                // For now, we will just sync the events.
            }

            // 3. Sync events for each calendar
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

            foreach (var evt in events)
            {
                if (evt.Title == "CANCELLED_TOMBSTONE")
                {
                    continue; 
                }

                evt.CalendarInfoId = calendarInfoId;

                var existing = existingEvents.FirstOrDefault(e => e.GoogleEventId == evt.GoogleEventId);
                if (existing != null)
                {
                    // Update existing event properties
                    existing.Title = evt.Title;
                    existing.Start = evt.Start;
                    existing.End = evt.End;
                    existing.IsAllDay = evt.IsAllDay;
                    existing.Location = evt.Location;
                    existing.Description = evt.Description;
                    
                    await _calendarRepository.UpdateEventAsync(existing, ct);
                }
                else
                {
                    await _calendarRepository.AddEventAsync(evt, ct);
                }
            }

            // Update sync state
            syncState.SyncToken = nextSyncToken;
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd = endDate;
            }

            if (isNewSyncState)
            {
                await _calendarRepository.AddSyncStateAsync(syncState, ct);
            }
            else
            {
                await _calendarRepository.SaveSyncStateAsync(syncState, ct);
            }

            // Commit transaction
            await _calendarRepository.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully synced {Count} events for {CalendarName}.", events.Count(), calendar.DisplayName);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer valid"))
        {
            _logger.LogWarning("Sync token expired for {CalendarName}. Clearing and restarting full sync.", calendar.DisplayName);
            
            // Clear token and retry
            syncState.SyncToken = null;
            if (isNewSyncState)
            {
                await _calendarRepository.AddSyncStateAsync(syncState, ct);
            }
            else
            {
                await _calendarRepository.SaveSyncStateAsync(syncState, ct);
            }
            await _calendarRepository.SaveChangesAsync(ct);
            
            await SyncAsync(calendarInfoId, startDate, endDate, ct);
        }
    }
}
