namespace FamilyHQ.Core.Models;

/// <summary>
/// The result of one <c>IGoogleCalendarClient.GetEventsAsync</c> call: the events it collected, the
/// sync token Google issued for the next incremental fetch, and whether the result is a complete
/// statement of what Google holds for the requested calendar and window.
/// </summary>
/// <param name="Events">
/// The events collected across every page the call read. Tombstones for cancelled items are
/// included, as the caller relies on Google naming the id it has removed.
/// </param>
/// <param name="NextSyncToken">
/// Google's <c>nextSyncToken</c> from the last page that carried one, or null when no page did.
/// </param>
/// <param name="IsComplete">
/// <b>False means this result is NOT a complete statement of what Google holds, so absence from
/// <see cref="Events"/> is not evidence that Google has deleted anything.</b> A caller that turns
/// absence into a delete — a tombstone diff, a prune — must refuse to act when this is false; one
/// that only upserts what it was given can ignore it.
/// <para>
/// Two things make it false, and both can happen without the call failing:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>The page cap was reached with a page token still outstanding.</b> The client stops after
///     a fixed number of <c>events.list</c> pages and returns what it has with a warning, so a
///     window holding more events than that budget comes back truncated.
///   </description></item>
///   <item><description>
///     <b>A page was skipped because its body did not deserialise.</b> A 200 response whose body
///     yields no event list is passed over rather than thrown on, so the fetch can come back short
///     — or, when it is the first page, empty — while still succeeding.
///   </description></item>
/// </list>
/// <para>
/// True means neither happened: every page Google offered was read and parsed.
/// </para>
/// </param>
public sealed record GoogleEventFetch(
    IEnumerable<CalendarEvent> Events,
    string? NextSyncToken,
    bool IsComplete);
