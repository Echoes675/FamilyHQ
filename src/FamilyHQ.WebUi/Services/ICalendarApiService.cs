using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.ViewModels;

namespace FamilyHQ.WebUi.Services;

public interface ICalendarApiService
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<MonthViewDto?> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task<CalendarEventViewModel?> CreateEventAsync(Guid calendarId, CreateEventRequest request, CancellationToken ct = default);
    Task<CalendarEventViewModel?> UpdateEventAsync(Guid calendarId, Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
    Task<bool> DeleteEventAsync(Guid calendarId, Guid eventId, CancellationToken ct = default);
    Task SimulateLoginAsync(CancellationToken ct = default);
}
