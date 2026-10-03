namespace FamilyHQ.Core.Calendar;

/// <summary>
/// One calendar an event could land on, reduced to the only two things
/// <see cref="OwningCalendarRule.OwningCalendarFor"/> is allowed to read.
/// </summary>
/// <remarks>
/// Deliberately not <c>CalendarInfo</c> or <c>CalendarSummaryViewModel</c>. The rule has to be
/// callable from both the Blazor client and the server without either side's types leaking into
/// <c>FamilyHQ.Core</c>, and a type carrying more than this would invite a future caller to route on
/// something the other side cannot see — a display order, a visibility flag, a sync state — which is
/// exactly the silent divergence having one rule is meant to prevent.
/// </remarks>
/// <param name="Id">The <c>CalendarInfo</c> id of the calendar.</param>
/// <param name="IsShared">Whether this is the household's shared calendar.</param>
public sealed record OwningCalendarCandidate(Guid Id, bool IsShared);
