using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Services;

public interface ICalendarApiService
{
    Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct = default);
    Task UpdateCalendarSettingsAsync(Guid calendarId, bool isVisible, bool isShared, CancellationToken ct = default);
    Task SaveCalendarOrderAsync(Dictionary<Guid, int> order, CancellationToken ct = default);
    Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task<CalendarEventViewModel> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task<CalendarEventViewModel> UpdateEventAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
    Task DeleteEventAsync(Guid eventId, CancellationToken ct = default);
    Task<CalendarEventViewModel> SetEventMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default);
}
