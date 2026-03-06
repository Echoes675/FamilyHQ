using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarRepository
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    Task<CalendarEvent?> GetEventByIdAsync(Guid id, CancellationToken ct = default);
    Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default);
    
    Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default);
    Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(Guid id, CancellationToken ct = default);
    
    Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
