using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarMigrationService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
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

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        // Capture the old ID before CreateEventAsync, which mutates calendarEvent.GoogleEventId
        // to the new Google-assigned ID on the target calendar.
        var oldGoogleEventId = calendarEvent.GoogleEventId;

        var created = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

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
}
