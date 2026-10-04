namespace FamilyHQ.Core.Models;

/// <summary>
/// The result of one <c>IGoogleCalendarClient.GetCalendarsAsync</c> call: the calendars it collected
/// and whether the result is a complete statement of the calendars Google holds for the account.
/// </summary>
/// <param name="Calendars">The calendars collected across every <c>calendarList</c> page the call read.</param>
/// <param name="IsComplete">
/// <b>False means this result is NOT a complete statement of what Google holds, so absence from
/// <see cref="Calendars"/> is not evidence that Google has deleted anything.</b> A caller that turns
/// absence into a delete — the obsolete-calendar prune — must refuse to act when this is false; one
/// that only adds or updates what it was given can ignore it.
/// <para>
/// Two things make it false, and both can happen without the call failing:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>The page cap was reached with a page token still outstanding.</b> The client follows
///     <c>nextPageToken</c> only up to a fixed number of pages, so an account past that budget
///     comes back truncated.
///   </description></item>
///   <item><description>
///     <b>A page was skipped because its body did not deserialise.</b> A 200 response that yields
///     no object at all, or one carrying no <c>items</c> array, produces no calendars for that page
///     — which is indistinguishable from an account that holds none, and so must not be read as one.
///   </description></item>
/// </list>
/// <para>
/// True means neither happened: every page Google offered was read and parsed, and every entry on
/// those pages became a <see cref="CalendarInfo"/>. There is deliberately no item-level cause here,
/// unlike <see cref="GoogleEventFetch.IsComplete"/>: the calendar-list mapping filters nothing, so
/// a page that parsed yields every calendar it named.
/// </para>
/// </param>
public sealed record GoogleCalendarFetch(
    IEnumerable<CalendarInfo> Calendars,
    bool IsComplete);
