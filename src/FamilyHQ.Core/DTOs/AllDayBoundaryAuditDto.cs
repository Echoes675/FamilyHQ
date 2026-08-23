namespace FamilyHQ.Core.DTOs;

/// <summary>
/// FHQ-174 read-only audit of the current user's stored all-day events.
/// </summary>
/// <remarks>
/// The counts are reported separately on purpose. The repository holds TWO unrelated legacy shapes
/// that both fail a "boundary is midnight UTC" test, and lumping them together would make the one
/// number an existing-data decision rests on unreadable:
/// <list type="bullet">
///   <item><description>
///     the FHQ-174 day shift — a boundary parsed with the host's UTC offset, which lands at
///     <c>23:00Z</c> on the previous day for a host at UTC+1; and
///   </description></item>
///   <item><description>
///     an <c>End</c> written as the INCLUSIVE end-of-day tick (<c>23:59:59.9999999</c>) instead of
///     Google's exclusive next-day midnight — a different defect, with different consequences, that
///     the kiosk still reads correctly.
///   </description></item>
/// </list>
/// A count on its own proves nothing about a cause; what it does is say where to look.
/// </remarks>
/// <param name="AllDayEvents">How many all-day rows were examined.</param>
/// <param name="NonMidnightStarts">
/// How many carry a <c>Start</c> that is not midnight UTC. This is the count that corresponds to the
/// FHQ-174 day shift: an all-day <c>Start</c> has no legitimate reason to sit anywhere else, so a
/// non-zero value here means rows were written while the host's UTC offset was not zero, and the
/// outbound <c>"yyyy-MM-dd"</c> formatting would name a different day from the one Google sent.
/// </param>
/// <param name="NonMidnightEnds">
/// How many carry an <c>End</c> that is neither midnight UTC nor the inclusive end-of-day tick
/// counted by <paramref name="InclusiveEndOfDayEnds"/> — i.e. the same day-shift signature, on the
/// other boundary.
/// </param>
/// <param name="InclusiveEndOfDayEnds">
/// How many carry the legacy inclusive end-of-day <c>End</c>. Reported so it can be recognised and
/// set aside; it is NOT evidence of a host-offset problem.
/// </param>
/// <param name="EarliestAffectedStart">
/// The <c>Start</c> of the earliest row counted in <paramref name="NonMidnightStarts"/> or
/// <paramref name="NonMidnightEnds"/>, or null when there are none. Rows inside the sync window heal
/// without intervention — <c>CalendarSyncService</c> overwrites <c>Start</c>/<c>End</c> from every
/// list response — so the range is what says whether the affected rows sit inside it or behind it.
/// </param>
/// <param name="LatestAffectedStart">The <c>Start</c> of the latest such row, or null when there are none.</param>
/// <param name="Truncated">
/// True when the user has more all-day rows than the audit examines in one pass, in which case every
/// count above is a lower bound taken over the earliest rows by <c>Start</c>. False is the expected
/// answer for any real family.
/// </param>
public record AllDayBoundaryAuditDto(
    int AllDayEvents,
    int NonMidnightStarts,
    int NonMidnightEnds,
    int InclusiveEndOfDayEnds,
    DateTimeOffset? EarliestAffectedStart,
    DateTimeOffset? LatestAffectedStart,
    bool Truncated);
