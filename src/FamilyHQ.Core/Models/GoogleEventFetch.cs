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
/// Google's <c>nextSyncToken</c> as the last successfully-read page reported it — which is null when
/// that page carried none. The client overwrites this value on every page it reads rather than
/// keeping the last non-null one, so this is the last page's answer and not "the last answer there
/// was". In practice Google sends the token only on the final page of a listing, so the two readings
/// coincide; the distinction is stated because the code makes the first one, not the second.
/// <para>
/// <b>Do not persist this when <see cref="IsComplete"/> is false</b>, whatever value it carries —
/// see that member for why.
/// </para>
/// </param>
/// <param name="IsComplete">
/// <b>False means this result is NOT a complete statement of what Google holds, so absence from
/// <see cref="Events"/> is not evidence that Google has deleted anything.</b> A caller that turns
/// absence into a delete — a tombstone diff, a prune — must refuse to act when this is false; one
/// that only upserts what it was given can ignore it.
/// <para>
/// <b>It also forbids advancing the sync token.</b> Persisting <see cref="NextSyncToken"/> declares
/// the local rows in step with Google up to that point, so the next fetch asks only for changes
/// since — and Google never re-sends an unchanged event. Storing it after an incomplete fetch turns
/// a gap into a permanent one, which is the mirror of the deletion hazard above and the more
/// damaging half: refusing to prune keeps a row that should go, whereas advancing the token loses an
/// event that is really there. <c>CalendarSyncService.SyncCoreAsync</c> therefore stores null when
/// this is false, which makes its next sync a full one.
/// </para>
/// <para>
/// Three things make it false, and all of them can happen without the call failing:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>The page cap was reached with a page token still outstanding.</b> The client stops after
///     a fixed number of <c>events.list</c> pages and returns what it has with a warning, so a
///     window holding more events than that budget comes back truncated.
///   </description></item>
///   <item><description>
///     <b>A page was skipped because its body did not deserialise.</b> A 200 response that yields
///     no object at all, or one carrying no <c>items</c> array, is passed over rather than thrown
///     on, so the fetch can come back short — or, when it is the first page, empty — while still
///     succeeding.
///   </description></item>
///   <item><description>
///     <b>An individual item was dropped because a boundary it needs did not resolve.</b> An item
///     whose <c>start</c> or <c>end</c> yields no instant, or whose all-day exception
///     <c>originalStartTime.date</c> is not an RFC 3339 full-date, is skipped with a warning rather
///     than thrown on — one unusable item must not stop a calendar syncing. The page itself was read
///     fine, so this is the cause that would otherwise be invisible: the answer looks complete and
///     omits an event Google still holds.
///   </description></item>
/// </list>
/// <para>
/// True means none of them happened: the client read every page Google offered and used every item
/// on them. Note what that does and does not claim — it is a statement about this client's reading
/// of Google's answer, not about the answer being current. A caller reading back a window it has
/// itself just written to needs its own guard for that; see
/// <c>CalendarEventService.PruneRowsAbsentFromWindowFetchAsync</c>.
/// </para>
/// </param>
public sealed record GoogleEventFetch(
    IEnumerable<CalendarEvent> Events,
    string? NextSyncToken,
    bool IsComplete);
