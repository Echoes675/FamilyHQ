namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Drops every item in a list whose trigger instant has passed, keeping one at the instant itself —
/// the boundary a per-minute re-filing tick needs to get right without a server call.
/// </summary>
/// <remarks>
/// Not currently wired into <c>Index.razor</c>'s reminders-timeline tick. While a row was one ping,
/// a fired one could simply be dropped locally and any other ping on the same event kept showing as
/// its own separate row. Now that a row summarises an event's reminders rather than carrying one
/// each, dropping it locally the same way could hide an event that still has a LATER reminder
/// outstanding, which only a fresh fetch (<c>RemindersController.GetUpcoming</c>,
/// <c>ReminderPingCalculator</c> server-side) can rule out — see <c>RefileRemindersForTick</c>'s own
/// remarks. The boundary this pins is still correct for anything filed by a single trigger instant
/// with nothing else depending on it, which is why it is kept rather than deleted.
/// </remarks>
public static class ReminderTickFilter
{
    /// <summary>
    /// Drops every ping whose trigger instant is strictly before <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// A ping exactly AT <paramref name="now"/> is kept, deliberately matching the "has this fired
    /// yet" boundary <c>RemindersController.GetUpcoming</c> applies server-side
    /// (a trigger instant <c>&gt;= now</c> has not fired): whichever side decides that instant has
    /// not fired yet, the other must not quietly disagree. Flipping this to a strict <c>&gt;</c> would
    /// drop a ping a full minute before its phone actually rings, for the one row whose trigger
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
