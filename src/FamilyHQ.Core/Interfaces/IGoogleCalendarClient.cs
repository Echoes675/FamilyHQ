using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IGoogleCalendarClient
{
    Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(string googleCalendarId, DateTimeOffset? syncWindowStart, DateTimeOffset? syncWindowEnd, string? syncToken = null, CancellationToken ct = default);
    Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);
}
