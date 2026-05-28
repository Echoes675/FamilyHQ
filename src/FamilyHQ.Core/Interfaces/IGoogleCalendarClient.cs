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

    /// <summary>
    /// Creates a recurring series master: the supplied RRULE line is sent in the <c>recurrence</c>
    /// array alongside the event's content (and content-hash extended property). Returns the event
    /// with its Google-assigned series id. Reused by FHQ-18.5 native series creation.
    /// </summary>
    Task<CalendarEvent> CreateRecurringEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, string rrule, CancellationToken ct = default);

    Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);
    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Patches only the <c>recurrence</c> array of a series master via events.patch, replacing it
    /// with the single supplied RRULE line. Used to truncate a series (apply <c>UNTIL</c>) for the
    /// "this and following" edit/delete split without disturbing any other master field.
    /// </summary>
    Task PatchSeriesRecurrenceAsync(string googleCalendarId, string seriesId, string rrule, CancellationToken ct = default);
    Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default);

    /// <summary>Returns null if the event is not found (404). Includes the content-hash extended property.</summary>
    Task<GoogleEventDetail?> GetEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a recurring series master via events.get and returns its RRULE line
    /// (the <c>RRULE:</c> entry from the master's <c>recurrence</c> array), or null
    /// when the master is missing (404) or carries no RRULE.
    /// </summary>
    Task<string?> GetSeriesMasterAsync(string googleCalendarId, string seriesId, CancellationToken ct = default);

    /// <summary>
    /// Creates a push-notification channel for calendar events via the Google Calendar watch API.
    /// </summary>
    Task<WatchChannelResponse> WatchEventsAsync(string googleCalendarId, string channelId, string webhookUrl, CancellationToken ct = default);

    /// <summary>
    /// Stops an existing push-notification channel.
    /// </summary>
    Task StopChannelAsync(string channelId, string resourceId, CancellationToken ct = default);
}
