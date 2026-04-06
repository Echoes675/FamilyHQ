using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarRepository
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default);
    Task<CalendarInfo?> GetSharedCalendarAsync(CancellationToken ct = default);

    // Returns events owned by calendarInfoId (used by sync service per-calendar).
    Task<IReadOnlyList<CalendarEvent>> GetEventsByOwnerCalendarAsync(Guid calendarInfoId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    // Returns all events for the current user (by owner calendar), including Members nav.
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<CalendarEvent?> GetEventByGoogleEventIdAsync(string googleEventId, CancellationToken ct = default);
    Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default);

    Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default);
    Task RemoveCalendarAsync(Guid calendarInfoId, CancellationToken ct = default);
    Task UpdateCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default);

    Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(Guid id, CancellationToken ct = default);

    Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default);
    Task AddSyncStateAsync(SyncState syncState, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
