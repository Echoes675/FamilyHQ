namespace FamilyHQ.Core.Models;

/// <summary>
/// The recurrence metadata of a series master fetched via events.get: the master's RRULE line and
/// its DTSTART. The start anchors forward-COUNT enumeration when a "this and following" split is
/// reshaped (FHQ-18.5 Part B) so the remaining occurrence count is measured from the true series
/// origin rather than the earliest locally-synced instance.
/// </summary>
public record SeriesMaster(
    string Rrule,
    DateTimeOffset Start);
