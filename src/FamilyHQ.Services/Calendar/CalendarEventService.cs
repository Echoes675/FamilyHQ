using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public async Task<CalendarEvent?> ReassignAsync(
        Guid fromCalendarId,
        Guid eventId,
        ReassignEventRequest request,
        CancellationToken ct = default)
    {
        var existing = await calendarRepository.GetEventAsync(eventId, ct);
        if (existing is null)
        {
            logger.LogWarning("Reassign failed: event {EventId} not found.", eventId);
            return null;
        }

        var fromCalendar = existing.Calendars.FirstOrDefault(c => c.Id == fromCalendarId);
        if (fromCalendar is null)
        {
            logger.LogWarning("Reassign failed: event {EventId} is not linked to calendar {CalendarId}.", eventId, fromCalendarId);
            return null;
        }

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var toCalendar = allCalendars.FirstOrDefault(c => c.Id == request.ToCalendarId);
        if (toCalendar is null)
        {
            logger.LogWarning("Reassign failed: target calendar {ToCalendarId} not found.", request.ToCalendarId);
            return null;
        }

        existing.Title = request.Title;
        existing.Start = request.Start;
        existing.End = request.End;
        existing.IsAllDay = request.IsAllDay;
        existing.Location = request.Location;
        existing.Description = request.Description;

        // Capture the old Google Event ID before CreateEventAsync mutates existing.GoogleEventId
        var oldGoogleEventId = existing.GoogleEventId;

        // Create on new calendar FIRST
        var created = await googleCalendarClient.CreateEventAsync(toCalendar.GoogleCalendarId, existing, ct);

        try
        {
            // Then delete from old calendar using the saved original event ID
            await googleCalendarClient.DeleteEventAsync(fromCalendar.GoogleCalendarId, oldGoogleEventId, ct);
        }
        catch (Exception)
        {
            // Rollback: delete the newly created event if the delete failed
            await googleCalendarClient.DeleteEventAsync(toCalendar.GoogleCalendarId, created.GoogleEventId, ct);
            throw;
        }

        existing.Calendars.Remove(fromCalendar);
        existing.Calendars.Add(toCalendar);

        try
        {
            await calendarRepository.UpdateEventAsync(existing, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Local DB update failed after Google operations succeeded.
            // The event is now in an inconsistent state: exists on new Google calendar,
            // deleted from old Google calendar, but local DB not updated.
            // Log for manual reconciliation.
            logger.LogError(ex,
                "Reassign failed: local DB update failed for event {EventId}. " +
                "Manual reconciliation may be required. Event exists on Google calendar {ToCalendarId} " +
                "but was deleted from {FromCalendarId}.",
                eventId, toCalendar.GoogleCalendarId, fromCalendar.GoogleCalendarId);
            throw;
        }

        logger.LogInformation(
            "Event {EventId} reassigned from calendar {FromCalendarId} to {ToCalendarId}.",
            eventId, fromCalendarId, request.ToCalendarId);

        return existing;
    }
}
