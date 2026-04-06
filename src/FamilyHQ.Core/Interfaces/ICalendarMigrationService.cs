using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarMigrationService
{
    /// <summary>
    /// Ensures the event lives on the correct calendar given its assigned members.
    /// Rule: 1 member → individual calendar; 2+ members → shared calendar.
    /// Migrates (create on target + delete on source) if the event is in the wrong place.
    /// Returns true if migration was performed.
    /// </summary>
    Task<bool> EnsureCorrectCalendarAsync(
        CalendarEvent calendarEvent,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default);
}
