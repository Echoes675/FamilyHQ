using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ICalendarMigrationService migrationService,
    IMemberTagParser memberTagParser,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var assignedMembers = request.MemberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new ArgumentException($"CalendarInfoId {id} is not known to the user.", nameof(request)))
            .ToList();

        // Determine target calendar
        var targetCalendar = assignedMembers.Count == 1
            ? assignedMembers[0]
            : await calendarRepository.GetSharedCalendarAsync(ct)
              ?? throw new InvalidOperationException("No shared calendar configured for multi-member events.");

        // Build description with member tag
        var memberNames = assignedMembers.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        var calendarEvent = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = fullDescription,
            OwnerCalendarInfoId = targetCalendar.Id,
            Members = assignedMembers
        };

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        calendarEvent = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        await calendarRepository.AddEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {GoogleEventId} created on calendar {CalendarId}.",
            calendarEvent.GoogleEventId, targetCalendar.GoogleCalendarId);

        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        // Preserve existing member tag; only update user-visible description
        var memberNames = calendarEvent.Members.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        calendarEvent.Title = request.Title;
        calendarEvent.Start = request.Start;
        calendarEvent.End = request.End;
        calendarEvent.IsAllDay = request.IsAllDay;
        calendarEvent.Location = request.Location;
        calendarEvent.Description = fullDescription;

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);
        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} updated.", eventId);
        return calendarEvent;
    }

    public async Task<CalendarEvent> SetMembersAsync(
        Guid eventId,
        IReadOnlyList<Guid> memberCalendarInfoIds,
        CancellationToken ct = default)
    {
        if (memberCalendarInfoIds.Count == 0)
            throw new ArgumentException("At least one member is required.", nameof(memberCalendarInfoIds));

        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var newMembers = memberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new ArgumentException($"CalendarInfoId {id} is not known to the user.", nameof(memberCalendarInfoIds)))
            .ToList();

        // Update description with new member tag
        var strippedDescription = memberTagParser.StripMemberTag(calendarEvent.Description);
        var memberNames = newMembers.Select(m => m.DisplayName).ToList();
        calendarEvent.Description = memberTagParser.NormaliseDescription(strippedDescription, memberNames);
        calendarEvent.Members = newMembers;

        // Migrate if the individual/shared invariant is violated.
        // EnsureCorrectCalendarAsync already writes to Google and saves the DB if it migrates.
        var migrated = await migrationService.EnsureCorrectCalendarAsync(calendarEvent, newMembers, ct);

        if (!migrated)
        {
            // No migration: write updated description/members to Google and DB.
            var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
                ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");
            var hash = EventContentHash.Compute(
                calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
                calendarEvent.IsAllDay, calendarEvent.Description);

            await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);
            await calendarRepository.UpdateEventAsync(calendarEvent, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }

        logger.LogInformation("Members updated for event {EventId}.", eventId);
        return calendarEvent;
    }

    public async Task DeleteAsync(Guid eventId, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        await googleCalendarClient.DeleteEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent.GoogleEventId, ct);
        await calendarRepository.DeleteEventAsync(eventId, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} deleted.", eventId);
    }
}
