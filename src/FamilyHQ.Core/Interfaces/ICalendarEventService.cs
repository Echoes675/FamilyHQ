using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarEventService
{
    /// <summary>
    /// Moves the event from <paramref name="fromCalendarId"/> to the calendar specified in
    /// <paramref name="request"/>, applying any updated fields in the same operation.
    /// Returns null if the event or either calendar is not found.
    /// </summary>
    Task<CalendarEvent?> ReassignAsync(
        Guid fromCalendarId,
        Guid eventId,
        ReassignEventRequest request,
        CancellationToken ct = default);
}
