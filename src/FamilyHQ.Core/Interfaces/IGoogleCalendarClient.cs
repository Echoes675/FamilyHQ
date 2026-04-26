using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IGoogleCalendarClient
{
    Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns events from the given calendar. Extended properties (content-hash) are included.
    /// </summary>
    Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(
        string googleCalendarId,
        DateTimeOffset? syncWindowStart,
        DateTimeOffset? syncWindowEnd,
        string? syncToken = null,
        CancellationToken ct = default);

    Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);
    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);
    Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default);

    /// <summary>Returns null if the event is not found (404). Includes the content-hash extended property.</summary>
    Task<GoogleEventDetail?> GetEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Creates a push-notification channel for calendar events via the Google Calendar watch API.
    /// </summary>
    Task<WatchChannelResponse> WatchEventsAsync(string googleCalendarId, string channelId, string webhookUrl, CancellationToken ct = default);

    /// <summary>
    /// Stops an existing push-notification channel.
    /// </summary>
    Task StopChannelAsync(string channelId, string resourceId, CancellationToken ct = default);
}
