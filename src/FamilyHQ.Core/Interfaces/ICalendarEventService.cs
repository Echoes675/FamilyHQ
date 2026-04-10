using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarEventService
{
    /// <summary>
    /// Creates an event. Determines correct calendar (individual or shared) from MemberCalendarInfoIds.
    /// Writes [members:...] tag to description and content-hash extended property.
    /// </summary>
    Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates event fields. Does not change members — use SetMembersAsync for that.
    /// </summary>
    Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full member list for an event. Rewrites the [members:...] tag, updates EventMembers,
    /// and migrates the event to the correct calendar if membership count crosses the 1/shared threshold.
    /// Throws <see cref="ArgumentException"/> if memberCalendarInfoIds is empty.
    /// </summary>
    Task<CalendarEvent> SetMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default);

    /// <summary>Deletes the event from Google Calendar and the local DB.</summary>
    Task DeleteAsync(Guid eventId, CancellationToken ct = default);
}
