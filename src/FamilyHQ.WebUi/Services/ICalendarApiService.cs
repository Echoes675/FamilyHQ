using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Services;

public interface ICalendarApiService
{
    Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct = default);
    Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task<CalendarEventViewModel> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task<CalendarEventViewModel> UpdateEventAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
    Task DeleteEventAsync(Guid eventId, CancellationToken ct = default);
    Task<CalendarEventViewModel> AddCalendarToEventAsync(Guid eventId, Guid calendarId, CancellationToken ct = default);
    Task RemoveCalendarFromEventAsync(Guid eventId, Guid calendarId, CancellationToken ct = default);
    Task<IEnumerable<CalendarEventViewModel>> GetEventsForRangeAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
    Task DeleteEventInstanceAsync(Guid masterEventId, string recurrenceId, string scope, CancellationToken ct = default);
    Task<CalendarEventViewModel> UpdateEventInstanceAsync(Guid masterEventId, string recurrenceId, string scope, UpdateEventRequest request, CancellationToken ct = default);
}
