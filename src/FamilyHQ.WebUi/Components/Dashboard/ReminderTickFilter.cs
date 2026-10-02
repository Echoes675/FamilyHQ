namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The reminders timeline's per-minute tick re-files its in-memory pings with no server call — this
/// is the one piece of that re-filing with a boundary worth getting wrong, so it is extracted here
/// rather than living only in <c>Index.razor</c>'s `@code` block, the same reason
/// <see cref="ReminderBucketing"/> and <see cref="RemindersViewLogic"/> sit beside it. There is no
/// bUnit in this repo, so logic kept in a Razor component is logic nothing exercises directly.
/// </summary>
public static class ReminderTickFilter
{
    /// <summary>
    /// Drops every ping whose trigger instant is strictly before <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// A ping exactly AT <paramref name="now"/> is kept, deliberately matching
    /// <c>RemindersController.GetUpcoming</c>'s own server-side filter
    /// (<c>r.TriggerAt &gt;= now &amp;&amp; r.TriggerAt &lt; displayEnd</c>): the server already
    /// decided that instant has not fired yet, and the client re-filing the same list on a later tick
    /// must not quietly disagree with the server that built it. Flipping this to a strict <c>&gt;</c>
    /// would drop a ping a full minute before its phone actually rings, for the one row whose trigger
    /// instant happens to land on a tick boundary.
    /// </remarks>
    /// <param name="pings">The rows to re-file. Never mutated — a new list is returned.</param>
    /// <param name="triggerAt">
    /// Reads the instant a row's phone notification fires. A <c>Func</c> rather than a view-model
    /// dependency, matching <see cref="ReminderBucketing.File{T}"/>'s own reason for taking one.
    /// </param>
    /// <param name="now">The instant to compare against — the tick's own clock reading, never wall time.</param>
    public static IReadOnlyList<T> DropFired<T>(
        IReadOnlyList<T> pings, Func<T, DateTimeOffset> triggerAt, DateTimeOffset now) =>
        pings.Where(p => triggerAt(p) >= now).ToList();
}
