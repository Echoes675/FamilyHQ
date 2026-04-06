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

        var sharedCal = await calendarRepository.GetSharedCalendarAsync(ct);

        var shouldBeShared = assignedMembers.Count > 1;
        var isCurrentlyShared = currentOwner.IsShared;

        if (shouldBeShared == isCurrentlyShared)
            return false;

        CalendarInfo targetCalendar;
        if (shouldBeShared)
        {
            if (sharedCal is null)
                throw new InvalidOperationException("No shared calendar is configured. Set IsShared = true on one calendar.");
            targetCalendar = sharedCal;
        }
        else
        {
            targetCalendar = assignedMembers.Single(m => !m.IsShared);
        }

        logger.LogInformation(
            "Migrating event {GoogleEventId} from {Source} to {Target}.",
            calendarEvent.GoogleEventId, currentOwner.GoogleCalendarId, targetCalendar.GoogleCalendarId);

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        var created = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        await googleCalendarClient.DeleteEventAsync(
            currentOwner.GoogleCalendarId, calendarEvent.GoogleEventId, ct);

        calendarEvent.GoogleEventId = created.GoogleEventId;
        calendarEvent.OwnerCalendarInfoId = targetCalendar.Id;

        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        return true;
    }
}
