using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarEventService
{
    /// <summary>
    /// Creates the event in Google Calendar and persists to DB.
    /// CalendarInfoIds[0] becomes the Google organiser.
    /// Throws ValidationException if any CalendarInfoId is unknown to the user.
    /// </summary>
    Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates event fields (title, times, location, description) via the owner calendar.
    /// Throws NotFoundException if the event is missing or its owner is not in the user's calendar set.
    /// </summary>
    Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Adds targetCalendarInfoId to the event's calendar set.
    /// Idempotent: returns the event unchanged if already linked, without calling Google.
    /// Throws NotFoundException if targetCalendarInfoId is not in the user's calendar set.
    /// </summary>
    Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct = default);

    /// <summary>
    /// Removes calendarInfoId from the event's calendar set.
    /// If it is the last calendar, delegates to DeleteAsync.
    /// Throws NotFoundException if calendarInfoId is not linked to the event.
    /// </summary>
    Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the event. Performs live external-attendee check:
    /// skips Google delete if external parties are present; deletes Google event otherwise.
    /// Always deletes local rows.
    /// </summary>
    Task DeleteAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Gets all events (including expanded recurring instances) for a date range.
    /// </summary>
    Task<IEnumerable<CalendarEventDto>> GetEventsForRangeAsync(
        DateTimeOffset start, 
        DateTimeOffset end, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a single instance of a recurring event (creates an exception).
    /// </summary>
    Task<CalendarEventDto> UpdateInstanceAsync(
        Guid masterEventId,
        string recurrenceId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates this and all following instances of a recurring event.
    /// Splits the series at the given recurrenceId.
    /// </summary>
    Task<CalendarEventDto> UpdateSeriesFromAsync(
        Guid masterEventId,
        string recurrenceId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates all instances of a recurring event (modifies the master).
    /// </summary>
    Task<CalendarEventDto> UpdateAllInSeriesAsync(
        Guid masterEventId,
        UpdateEventRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a single instance of a recurring event (adds an EXDATE exception).
    /// </summary>
    Task DeleteInstanceAsync(
        Guid masterEventId,
        string recurrenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes this and all following instances of a recurring event.
    /// </summary>
    Task DeleteSeriesFromAsync(
        Guid masterEventId,
        string recurrenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all instances of a recurring event (deletes the master).
    /// </summary>
    Task DeleteAllInSeriesAsync(
        Guid masterEventId,
        CancellationToken cancellationToken = default);
}
