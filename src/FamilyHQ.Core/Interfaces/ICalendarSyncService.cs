using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarSyncService
{
    Task SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
    Task SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
}
