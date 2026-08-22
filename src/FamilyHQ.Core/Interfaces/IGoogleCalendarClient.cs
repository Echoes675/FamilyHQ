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

    /// <summary>
    /// Patches only the fields present in the request body (events.patch, HTTP PATCH — a partial
    /// MERGE). Fields absent from the body are preserved server-side, so Google's attendees,
    /// reminders, colorId and recurrence survive a kiosk edit. This is the only event-field write
    /// path; there is deliberately no full-resource-replace (events.update / PUT) sibling.
    /// </summary>
    Task<CalendarEvent> PatchEventFieldsAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);

    /// <summary>
    /// FHQ-172. As <see cref="PatchEventFieldsAsync"/>, except the request body carries <b>no</b>
    /// <c>start</c> and <c>end</c> keys at all, so events.patch's merge semantics leave the
    /// resource's own start and end exactly as Google holds them.
    /// </summary>
    /// <remarks>
    /// Used for a series master whose true DTSTART could not be established. The alternative — the
    /// earliest locally-synced row — is a proxy that relocates the series' origin forward and
    /// deletes every occurrence before the sync window, on every device. Omitting the fields writes
    /// only what the user actually changed, which is what the prime directive requires; a request
    /// that DOES change the timing cannot be honoured this way and is refused by the caller instead.
    /// Returns nothing: the event handed in is not the resource's post-write state, because its
    /// start and end were never sent.
    /// <para>
    /// <b>Reachability, stated honestly.</b> This is a defence-in-depth path with no demonstrated
    /// production trigger. Since <see cref="GetSeriesMasterAsync"/> stopped discarding a master's
    /// start merely because it carried no RRULE, the only remaining way for a caller to reach here
    /// is a master events.get that 404s or yields no parsable start — and an events.patch to that
    /// same id would 404 too, so the call would fail rather than quietly do the wrong thing. It is
    /// kept because the failure it guards against is irreversible loss of a family's series history,
    /// not because it is the fix for the reported defect.
    /// </para>
    /// </remarks>
    Task PatchEventFieldsPreservingTimesAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);

    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Patches only the <c>recurrence</c> array of a series master via events.patch, replacing it
    /// with the single supplied RRULE line. Used to truncate a series (apply <c>UNTIL</c>) for the
    /// "this and following" edit/delete split without disturbing any other master field.
    /// </summary>
    Task PatchSeriesRecurrenceAsync(string googleCalendarId, string seriesId, string rrule, CancellationToken ct = default);

    /// <summary>
    /// Clears the <c>recurrence</c> array of a series master via events.patch (sends an empty array),
    /// collapsing the series back to a single, non-recurring event without disturbing any other
    /// master field. Used by the FHQ-18.5 "recurrence off" toggle.
    /// </summary>
    Task ClearSeriesRecurrenceAsync(string googleCalendarId, string seriesId, CancellationToken ct = default);

    Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default);

    /// <summary>Returns null if the event is not found (404). Includes the content-hash extended property.</summary>
    Task<GoogleEventDetail?> GetEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a recurring series master via events.get and returns the master's DTSTART, the zone
    /// it is anchored to, and its RRULE line (the <c>RRULE:</c> entry from the master's
    /// <c>recurrence</c> array). The start anchors forward-COUNT enumeration for "this and
    /// following" splits, and the AllInSeries edit's shift of the series origin.
    /// <para>
    /// Null only when the master is missing (404) or has no resolvable start — with no start there
    /// is no anchor. A master with a start but <b>no RRULE line</b> (an RDATE-only import) is
    /// returned with <see cref="SeriesMaster.Rrule"/> null rather than discarded (FHQ-172).
    /// </para>
    /// </summary>
    Task<SeriesMaster?> GetSeriesMasterAsync(string googleCalendarId, string seriesId, CancellationToken ct = default);

    /// <summary>
    /// Creates a push-notification channel for calendar events via the Google Calendar watch API.
    /// </summary>
    Task<WatchChannelResponse> WatchEventsAsync(string googleCalendarId, string channelId, string webhookUrl, string channelToken, CancellationToken ct = default);

    /// <summary>
    /// Stops an existing push-notification channel.
    /// </summary>
    Task StopChannelAsync(string channelId, string resourceId, CancellationToken ct = default);
}
