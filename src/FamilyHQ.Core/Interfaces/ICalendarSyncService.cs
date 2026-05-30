using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarSyncService
{
    Task<SyncResult> SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
    Task<SyncResult> SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
}
