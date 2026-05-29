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

    /// <summary>
    /// Series equivalent of <see cref="EnsureCorrectCalendarAsync"/> for an "All events" edit that
    /// crosses the 1↔N membership boundary. Inserts the series on the correct target calendar (with
    /// the supplied RRULE and a normalised members tag on the master), receives the new Google series
    /// id, repoints every local instance in the sync window to the new id and owner calendar, and
    /// deletes the old series from Google. An outbound-write hash is recorded for every touched
    /// instance so the resulting webhooks are recognised as self-echoes.
    /// Returns true if a migration was performed; false if the series is already on the correct calendar.
    /// </summary>
    Task<bool> EnsureCorrectCalendarForSeriesAsync(
        string seriesId,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default);
}
