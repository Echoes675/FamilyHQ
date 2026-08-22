namespace FamilyHQ.Core.Models;

/// <summary>
/// The recurrence metadata of a series master fetched via events.get: the master's RRULE line, its
/// DTSTART, and the IANA zone that DTSTART is anchored to. The start anchors forward-COUNT
/// enumeration when a "this and following" split is reshaped (FHQ-18.5 Part B) so the remaining
/// occurrence count is measured from the true series origin rather than the earliest locally-synced
/// instance.
/// </summary>
/// <param name="Rrule">
/// The master's RRULE line, e.g. <c>RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5</c>.
/// <para>
/// <b>Null when the master's <c>recurrence</c> array carries no <c>RRULE:</c> line</b> — an
/// RDATE-only master (the shape an ICS/CalDAV import produces) is the real-world case. Such a master
/// still exists and still has a DTSTART, so it is returned rather than discarded: the anchor is the
/// value FHQ-172 is about, and throwing the whole record away for the sake of a field the anchor
/// caller never reads was the one shape that could relocate a series' origin while its master sat
/// alive in Google. (Established by tracing the code, not from an observed incident — but it is the
/// shape the write paths could actually reach.) A caller that needs the rule must handle null; a
/// caller that needs only the origin is unaffected.
/// </para>
/// </param>
/// <param name="Start">The master's DTSTART as an absolute instant.</param>
/// <param name="TimeZone">
/// Google's <c>start.timeZone</c> for the master (e.g. <c>Europe/London</c>). The recurrence is
/// anchored to this zone's WALL CLOCK, so it is required to enumerate the series correctly across a
/// DST transition (FHQ-161). Null when Google supplied none — all-day masters carry no zone, and
/// they are date-anchored so DST cannot move them.
/// </param>
public record SeriesMaster(
    string? Rrule,
    DateTimeOffset Start,
    string? TimeZone = null);
