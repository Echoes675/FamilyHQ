using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

// TODO(Task 8): Full rewrite — this stub satisfies ICalendarEventService compilation only.
public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ICalendarMigrationService calendarMigrationService,
    IMemberTagParser memberTagParser,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Task 8: rewrite pending.");

    public Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("Task 8: rewrite pending.");

    public Task<CalendarEvent> SetMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default)
        => throw new NotImplementedException("Task 8: rewrite pending.");

    public Task DeleteAsync(Guid eventId, CancellationToken ct = default)
        => throw new NotImplementedException("Task 8: rewrite pending.");
}
