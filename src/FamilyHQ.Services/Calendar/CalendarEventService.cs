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
    public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        foreach (var calendarInfoId in request.CalendarInfoIds)
        {
            if (!calendarLookup.ContainsKey(calendarInfoId))
                throw new InvalidOperationException($"CalendarInfoId {calendarInfoId} is not known to the user.");
        }

        var ownerCalendar = calendarLookup[request.CalendarInfoIds[0]];

        var calendarEvent = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = request.Description,
            OwnerCalendarInfoId = ownerCalendar.Id,
            Members = request.CalendarInfoIds.Select(id => calendarLookup[id]).ToList()
        };

        calendarEvent = await googleCalendarClient.CreateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, ct);

        if (request.CalendarInfoIds.Count > 1)
        {
            var attendeeIds = request.CalendarInfoIds
                .Skip(1)
                .Select(id => calendarLookup[id].GoogleCalendarId);

            await googleCalendarClient.PatchEventAttendeesAsync(
                ownerCalendar.GoogleCalendarId,
                calendarEvent.GoogleEventId,
                attendeeIds,
                ct);
        }

        try
        {
            await calendarRepository.AddEventAsync(calendarEvent, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "CreateAsync failed: local DB save failed for event with GoogleEventId {GoogleEventId}. " +
                "Manual reconciliation may be required. Event exists on Google calendar {OwnerCalendarId}.",
                calendarEvent.GoogleEventId, ownerCalendar.GoogleCalendarId);
            throw;
        }

        logger.LogInformation(
            "Event {GoogleEventId} created on calendar {OwnerCalendarId}.",
            calendarEvent.GoogleEventId, ownerCalendar.GoogleCalendarId);

        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} for event {eventId} is not in the user's calendar set.");

        calendarEvent.Title = request.Title;
        calendarEvent.Start = request.Start;
        calendarEvent.End = request.End;
        calendarEvent.IsAllDay = request.IsAllDay;
        calendarEvent.Location = request.Location;
        calendarEvent.Description = request.Description;

        await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, ct);

        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Event {EventId} updated via calendar {OwnerCalendarId}.",
            eventId, ownerCalendar.GoogleCalendarId);

        return calendarEvent;
    }

    public async Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        if (calendarEvent.Members.Any(c => c.Id == targetCalendarInfoId))
        {
            logger.LogInformation(
                "Event {EventId} is already linked to calendar {CalendarInfoId}. No action taken.",
                eventId, targetCalendarInfoId);
            return calendarEvent;
        }

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var targetCalendar = allCalendars.FirstOrDefault(c => c.Id == targetCalendarInfoId)
            ?? throw new InvalidOperationException($"CalendarInfoId {targetCalendarInfoId} is not in the user's calendar set.");

        var ownerCalendar = allCalendars.First(c => c.Id == calendarEvent.OwnerCalendarInfoId);

        calendarEvent.Members.Add(targetCalendar);

        var attendeeIds = calendarEvent.Members
            .Where(c => c.Id != calendarEvent.OwnerCalendarInfoId)
            .Select(c => c.GoogleCalendarId);

        await googleCalendarClient.PatchEventAttendeesAsync(
            ownerCalendar.GoogleCalendarId,
            calendarEvent.GoogleEventId,
            attendeeIds,
            ct);

        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Calendar {CalendarInfoId} added to event {EventId}.",
            targetCalendarInfoId, eventId);

        return calendarEvent;
    }

    public async Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        if (calendarEvent.Members.Count == 1)
        {
            await DeleteAsync(eventId, ct);
            return;
        }

        var calendarToRemove = calendarEvent.Members.FirstOrDefault(c => c.Id == calendarInfoId)
            ?? throw new InvalidOperationException($"CalendarInfoId {calendarInfoId} is not linked to event {eventId}.");

        calendarEvent.Members.Remove(calendarToRemove);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarDict = allCalendars.ToDictionary(c => c.Id);

        if (calendarInfoId == calendarEvent.OwnerCalendarInfoId)
        {
            var newOwnerInfo = calendarEvent.Members.First();
            var newOwnerCalendar = calendarDict[newOwnerInfo.Id];
            var removedOwnerGoogleId = calendarDict[calendarInfoId].GoogleCalendarId;

            await googleCalendarClient.MoveEventAsync(
                removedOwnerGoogleId,
                calendarEvent.GoogleEventId,
                newOwnerCalendar.GoogleCalendarId,
                ct);

            calendarEvent.OwnerCalendarInfoId = newOwnerInfo.Id;

            var attendeeIds = calendarEvent.Members
                .Where(c => c.Id != newOwnerInfo.Id)
                .Select(c => calendarDict[c.Id].GoogleCalendarId);

            await googleCalendarClient.PatchEventAttendeesAsync(
                newOwnerCalendar.GoogleCalendarId,
                calendarEvent.GoogleEventId,
                attendeeIds,
                ct);
        }
        else
        {
            var ownerCalendar = calendarDict[calendarEvent.OwnerCalendarInfoId];

            var attendeeIds = calendarEvent.Members
                .Where(c => c.Id != calendarEvent.OwnerCalendarInfoId)
                .Select(c => calendarDict[c.Id].GoogleCalendarId);

            await googleCalendarClient.PatchEventAttendeesAsync(
                ownerCalendar.GoogleCalendarId,
                calendarEvent.GoogleEventId,
                attendeeIds,
                ct);
        }

        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Calendar {CalendarInfoId} removed from event {EventId}.",
            calendarInfoId, eventId);
    }

    public async Task DeleteAsync(Guid eventId, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.First(c => c.Id == calendarEvent.OwnerCalendarInfoId);

        var googleEvent = await googleCalendarClient.GetEventAsync(
            ownerCalendar.GoogleCalendarId,
            calendarEvent.GoogleEventId,
            ct);

        if (googleEvent is not null)
        {
            var userCalendarIds = allCalendars.Select(c => c.GoogleCalendarId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasExternalAttendees = googleEvent.AttendeeEmails.Any(email =>
                !userCalendarIds.Contains(email) &&
                !string.Equals(email, googleEvent.OrganizerEmail, StringComparison.OrdinalIgnoreCase));

            if (!hasExternalAttendees)
            {
                await googleCalendarClient.DeleteEventAsync(
                    ownerCalendar.GoogleCalendarId,
                    calendarEvent.GoogleEventId,
                    ct);
            }
            else
            {
                logger.LogInformation(
                    "Event {EventId} has external attendees; skipping Google delete.",
                    eventId);
            }
        }
        else
        {
            logger.LogWarning(
                "Event {EventId} with GoogleEventId {GoogleEventId} not found on Google; skipping Google delete.",
                eventId, calendarEvent.GoogleEventId);
        }

        await calendarRepository.DeleteEventAsync(eventId, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} deleted locally.", eventId);
    }
}
