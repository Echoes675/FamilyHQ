using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IGoogleCalendarClient
{
    Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(string googleCalendarId, DateTimeOffset? syncWindowStart, DateTimeOffset? syncWindowEnd, string? syncToken = null, CancellationToken ct = default);
    Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);
    Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default);

    /// <summary>
    /// Full replacement of the attendees array. attendeeGoogleCalendarIds must contain ALL
    /// calendars EXCEPT the organiser — Google keeps organiser and attendees as separate fields.
    /// </summary>
    Task PatchEventAttendeesAsync(
        string organizerCalendarId,
        string googleEventId,
        IEnumerable<string> attendeeGoogleCalendarIds,
        CancellationToken ct);

    /// <summary>Returns null if the event is not found (404).</summary>
    Task<GoogleEventDetail?> GetEventAsync(
        string googleCalendarId,
        string googleEventId,
        CancellationToken ct);
}
