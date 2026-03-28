using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    IRruleExpander rruleExpander,
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
            Calendars = request.CalendarInfoIds.Select(id => calendarLookup[id]).ToList()
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

        if (calendarEvent.Calendars.Any(c => c.Id == targetCalendarInfoId))
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

        calendarEvent.Calendars.Add(targetCalendar);

        var attendeeIds = calendarEvent.Calendars
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

        if (calendarEvent.Calendars.Count == 1)
        {
            await DeleteAsync(eventId, ct);
            return;
        }

        var calendarToRemove = calendarEvent.Calendars.FirstOrDefault(c => c.Id == calendarInfoId)
            ?? throw new InvalidOperationException($"CalendarInfoId {calendarInfoId} is not linked to event {eventId}.");

        calendarEvent.Calendars.Remove(calendarToRemove);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarDict = allCalendars.ToDictionary(c => c.Id);

        if (calendarInfoId == calendarEvent.OwnerCalendarInfoId)
        {
            var newOwnerInfo = calendarEvent.Calendars.First();
            var newOwnerCalendar = calendarDict[newOwnerInfo.Id];
            var removedOwnerGoogleId = calendarDict[calendarInfoId].GoogleCalendarId;

            await googleCalendarClient.MoveEventAsync(
                removedOwnerGoogleId,
                calendarEvent.GoogleEventId,
                newOwnerCalendar.GoogleCalendarId,
                ct);

            calendarEvent.OwnerCalendarInfoId = newOwnerInfo.Id;

            var attendeeIds = calendarEvent.Calendars
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

            var attendeeIds = calendarEvent.Calendars
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

    public async Task<IEnumerable<CalendarEventDto>> GetEventsForRangeAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        // Fetch all non-exception events that overlap with the range (including master recurring events)
        var allEvents = await calendarRepository.GetEventsAsync(start, end, cancellationToken);

        // Separate master recurring events from non-recurring events
        var masterRecurringEvents = allEvents
            .Where(e => !string.IsNullOrEmpty(e.RecurrenceRule) && !e.IsRecurrenceException)
            .ToList();

        // Fetch all exception instances for the range
        var exceptionEvents = allEvents
            .Where(e => e.IsRecurrenceException)
            .ToList();

        // Build a lookup for exceptions by master event ID and recurrence ID
        var exceptionLookup = exceptionEvents
            .Where(e => e.MasterEventId.HasValue)
            .GroupBy(e => e.MasterEventId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(e => e.RecurrenceId!));

        // Expand recurring events and merge with exceptions
        var result = new List<CalendarEventDto>();

        foreach (var masterEvent in masterRecurringEvents)
        {
            // Expand the recurring event
            var expandedInstances = rruleExpander.ExpandRecurringEvent(masterEvent, start, end);

            foreach (var instance in expandedInstances)
            {
                // Check if there's an exception for this instance
                if (exceptionLookup.TryGetValue(masterEvent.Id, out var exceptionsByRecurrenceId)
                    && exceptionsByRecurrenceId.TryGetValue(instance.RecurrenceId!, out var exception))
                {
                    // Use the exception event instead of the expanded instance
                    result.Add(MapToDto(exception));
                }
                else
                {
                    // Use the expanded instance
                    result.Add(MapToDto(masterEvent, instance));
                }
            }
        }

        // Add non-recurring events
        var nonRecurringEvents = allEvents
            .Where(e => string.IsNullOrEmpty(e.RecurrenceRule) && !e.IsRecurrenceException);

        foreach (var evt in nonRecurringEvents)
        {
            result.Add(MapToDto(evt));
        }

        // Add exception events that don't have a matching master (edge case)
        foreach (var exception in exceptionEvents)
        {
            if (!masterRecurringEvents.Any(m => m.Id == exception.MasterEventId))
            {
                result.Add(MapToDto(exception));
            }
        }

        return result;
    }

    public async Task<CalendarEventDto> UpdateInstanceAsync(
        Guid masterEventId,
        string recurrenceId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("UpdateInstanceAsync not yet implemented. MasterEventId={MasterEventId}, RecurrenceId={RecurrenceId}", masterEventId, recurrenceId);
        throw new NotImplementedException("UpdateInstanceAsync is not yet implemented. This requires complex Google Calendar sync logic for creating exception instances.");
    }

    public async Task<CalendarEventDto> UpdateSeriesFromAsync(
        Guid masterEventId,
        string recurrenceId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("UpdateSeriesFromAsync not yet implemented. MasterEventId={MasterEventId}, RecurrenceId={RecurrenceId}", masterEventId, recurrenceId);
        throw new NotImplementedException("UpdateSeriesFromAsync is not yet implemented. This requires complex Google Calendar sync logic for splitting a series.");
    }

    public async Task<CalendarEventDto> UpdateAllInSeriesAsync(
        Guid masterEventId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var masterEvent = await calendarRepository.GetEventAsync(masterEventId, cancellationToken)
            ?? throw new InvalidOperationException($"Master event {masterEventId} not found.");

        masterEvent.Title = request.Title;
        masterEvent.Start = request.Start;
        masterEvent.End = request.End;
        masterEvent.IsAllDay = request.IsAllDay;
        masterEvent.Location = request.Location;
        masterEvent.Description = request.Description;
        masterEvent.RecurrenceRule = request.RecurrenceRule;

        var allCalendars = await calendarRepository.GetCalendarsAsync(cancellationToken);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == masterEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {masterEvent.OwnerCalendarInfoId} for event {masterEventId} is not in the user's calendar set.");

        await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, masterEvent, cancellationToken);
        await calendarRepository.UpdateEventAsync(masterEvent, cancellationToken);
        await calendarRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "All instances of recurring event {EventId} updated via calendar {OwnerCalendarId}.",
            masterEventId, ownerCalendar.GoogleCalendarId);

        return MapToDto(masterEvent);
    }

    public async Task DeleteInstanceAsync(
        Guid masterEventId,
        string recurrenceId,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("DeleteInstanceAsync not yet implemented. MasterEventId={MasterEventId}, RecurrenceId={RecurrenceId}", masterEventId, recurrenceId);
        throw new NotImplementedException("DeleteInstanceAsync is not yet implemented. This requires complex Google Calendar sync logic for adding EXDATE exceptions.");
    }

    public async Task DeleteSeriesFromAsync(
        Guid masterEventId,
        string recurrenceId,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning("DeleteSeriesFromAsync not yet implemented. MasterEventId={MasterEventId}, RecurrenceId={RecurrenceId}", masterEventId, recurrenceId);
        throw new NotImplementedException("DeleteSeriesFromAsync is not yet implemented. This requires complex Google Calendar sync logic for deleting from a specific instance onwards.");
    }

    public async Task DeleteAllInSeriesAsync(
        Guid masterEventId,
        CancellationToken cancellationToken = default)
    {
        var masterEvent = await calendarRepository.GetEventAsync(masterEventId, cancellationToken)
            ?? throw new InvalidOperationException($"Master event {masterEventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(cancellationToken);
        var ownerCalendar = allCalendars.First(c => c.Id == masterEvent.OwnerCalendarInfoId);

        // Delete the master event from Google Calendar
        var googleEvent = await googleCalendarClient.GetEventAsync(
            ownerCalendar.GoogleCalendarId,
            masterEvent.GoogleEventId,
            cancellationToken);

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
                    masterEvent.GoogleEventId,
                    cancellationToken);
            }
            else
            {
                logger.LogInformation(
                    "Recurring event {EventId} has external attendees; skipping Google delete.",
                    masterEventId);
            }
        }

        // Delete the master event and all its exceptions from the local DB
        await calendarRepository.DeleteEventAsync(masterEventId, cancellationToken);
        
        // Also delete any exception events that reference this master
        // Use a very wide date range to catch all exceptions
        var allExceptions = await calendarRepository.GetEventsAsync(
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            cancellationToken);
        var exceptionsToDelete = allExceptions
            .Where(e => e.MasterEventId == masterEventId)
            .ToList();

        foreach (var exception in exceptionsToDelete)
        {
            await calendarRepository.DeleteEventAsync(exception.Id, cancellationToken);
        }

        await calendarRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("All instances of recurring event {EventId} deleted locally.", masterEventId);
    }

    private static CalendarEventDto MapToDto(CalendarEvent evt)
    {
        return new CalendarEventDto(
            evt.Id,
            evt.GoogleEventId,
            evt.Title,
            evt.Start,
            evt.End,
            evt.IsAllDay,
            evt.Location,
            evt.Description,
            evt.Calendars
                .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
                .ToList(),
            evt.RecurrenceRule,
            evt.RecurrenceId,
            evt.IsRecurrenceException,
            evt.MasterEventId);
    }

    private static CalendarEventDto MapToDto(CalendarEvent masterEvent, CalendarEvent instance)
    {
        return new CalendarEventDto(
            masterEvent.Id,
            masterEvent.GoogleEventId,
            masterEvent.Title,
            instance.Start,
            instance.End,
            masterEvent.IsAllDay,
            masterEvent.Location,
            masterEvent.Description,
            masterEvent.Calendars
                .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
                .ToList(),
            masterEvent.RecurrenceRule,
            instance.RecurrenceId,
            instance.IsRecurrenceException,
            instance.MasterEventId);
    }
}
