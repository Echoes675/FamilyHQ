using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.WebUi.Services;

public interface ICalendarApiService
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<MonthViewDto?> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task SimulateLoginAsync(CancellationToken ct = default);
}
