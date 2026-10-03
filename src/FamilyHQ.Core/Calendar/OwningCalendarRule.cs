namespace FamilyHQ.Core.Calendar;

/// <summary>
/// The one statement of which Google calendar an event with a given set of member chips lands on:
/// the single chosen member's own calendar, or the household's shared calendar when several members
/// are chosen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is here and not at either call site.</b> Two places need the answer. The server applies
/// it for real when it creates the event (<c>CalendarEventService.CreateAsync</c>). The event modal
/// has to predict it for an event that is not saved yet, because default reminders belong to a
/// <i>calendar</i>, so the Reminders tab cannot say what inheriting the calendar's reminders will do
/// until it knows which calendar that is. An existing event does not need the prediction — the
/// server reports its owner and the modal reads that — but a new one has no owner yet while its
/// chips are still being chosen.
/// </para>
/// <para>
/// Those two implementations agreed, with nothing tying them together. A change to the server's
/// routing would therefore have shipped silently and surfaced as the Reminders tab pre-filling the
/// wrong values, reading as a reminders bug rather than a routing one. This type exists so the rule
/// is written once; <c>CalendarEventServiceOwningCalendarAgreementTests</c> is what keeps the
/// server's own copy pinned to it.
/// </para>
/// </remarks>
public static class OwningCalendarRule
{
    /// <summary>
    /// The calendar an event assigned to <paramref name="selectedCalendarIds"/> lands on, or
    /// <c>null</c> when the rule has no calendar to name.
    /// </summary>
    /// <param name="selectedCalendarIds">
    /// The member chips currently chosen. The count is taken exactly as supplied and is not
    /// de-duplicated: <c>CreateEventRequestValidator</c> rejects a request with duplicate ids
    /// ("Duplicate calendar IDs are not allowed"), and the server likewise counts one assigned member
    /// per id it was handed, so collapsing duplicates here would be this function disagreeing with
    /// the create path rather than matching it.
    /// </param>
    /// <param name="candidates">
    /// The calendars available to land on. An <see cref="IReadOnlyCollection{T}"/> rather than a
    /// list because the answer deliberately does not depend on the order they arrive in — see the
    /// tie-break note below.
    /// </param>
    /// <returns>
    /// <para>
    /// The chosen member's id for a single selection; the shared calendar's id for several. Null in
    /// three cases, each of which is "no answer available" rather than a second opinion the server
    /// could contradict:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Nothing selected — <c>CreateEventRequestValidator</c> rejects a memberless create ("At least
    /// one calendar is required") before the server's create path runs, so there is no server answer
    /// to differ from. (The memberless fallback elsewhere in the codebase belongs to events arriving
    /// <i>from</i> Google during sync, not to this path.)
    /// </description></item>
    /// <item><description>
    /// A single selection naming a calendar absent from <paramref name="candidates"/> — the server
    /// throws <c>UnknownCalendarException</c> for an id missing from its own calendar list.
    /// </description></item>
    /// <item><description>
    /// Several selected with no shared calendar among the candidates — the server throws
    /// <c>InvalidOperationException</c> ("No shared calendar configured for multi-member events").
    /// </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <b>The tie-break.</b> Among several shared candidates the lowest <see cref="Guid"/> wins. That
    /// state cannot occur today — <c>CalendarsController</c> clears the previous shared calendar
    /// whenever one is designated — and with one shared calendar the tie-break is unreachable. It is
    /// pinned anyway because the two callers hold their candidates in different orders (the dashboard
    /// in display order; the repository's query applies no ordering at all), so "the first shared one
    /// I was handed" would make the client and the server name different calendars the moment that
    /// single-shared enforcement slipped. An intrinsic total order is the only tie-break both sides
    /// can honour.
    /// </remarks>
    public static Guid? OwningCalendarFor(
        IReadOnlyCollection<Guid> selectedCalendarIds,
        IReadOnlyCollection<OwningCalendarCandidate> candidates) =>
        selectedCalendarIds.Count switch
        {
            1 => candidates.FirstOrDefault(c => c.Id == selectedCalendarIds.First())?.Id,
            > 1 => candidates.Where(c => c.IsShared).OrderBy(c => c.Id).FirstOrDefault()?.Id,
            _ => null
        };
}
