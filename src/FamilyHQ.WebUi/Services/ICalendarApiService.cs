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

    // FHQ-18: recurring-series edit/delete at a Google-parity scope. These proxy the matching
    // ICalendarEventService methods through the WebApi /api/events/{id}/recurring endpoints.
    Task<CalendarEventViewModel> UpdateRecurringEventAsync(Guid eventId, UpdateEventRequest request, RecurrenceScope scope, CancellationToken ct = default);
    Task DeleteRecurringEventAsync(Guid eventId, RecurrenceScope scope, CancellationToken ct = default);
    Task<CalendarEventViewModel> SetEventMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default);
    Task TriggerSyncAsync(CancellationToken ct = default);
    Task RegisterWebhooksAsync(CancellationToken ct = default);
    Task<ConnectionStatusDto?> GetConnectionStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Every event, across every calendar, that has at least one reminder still due to fire between
    /// now and the end of next month — one row per event, never one per reminder.
    /// </summary>
    Task<IReadOnlyList<UpcomingReminderEventViewModel>> GetUpcomingRemindersAsync(CancellationToken ct = default);

    /// <summary>
    /// One event by id, for a caller holding an id but no event — an event in a month the dashboard
    /// has never loaded is not in memory to be read. Null on a 404 rather than a throw: an event can
    /// be deleted on a phone between whatever named the id and the fetch for it, which is ordinary.
    /// </summary>
    Task<CalendarEventViewModel?> GetEventAsync(Guid eventId, CancellationToken ct = default);
}
