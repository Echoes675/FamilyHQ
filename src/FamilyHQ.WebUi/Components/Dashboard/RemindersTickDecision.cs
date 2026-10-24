namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>What one reminders tick should do.</summary>
/// <param name="Refetch">Ask the server again, because the anchor it gave has expired.</param>
/// <param name="Render">Redraw, because the date the view measures against has moved.</param>
public readonly record struct RemindersTickOutcome(bool Refetch, bool Render);

/// <summary>
/// The per-minute reminders tick, as a decision rather than a method on a component.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a refetch replaced the month-change rule.</b> The tick used to re-read "today" off the
/// kiosk's clock and refetch only when the MONTH changed, because the only thing memory could not
/// resolve was the window's far edge moving. The anchor now comes from the server, so what expires is
/// the anchor itself — and a rollover is the instant it does. The month rule disappears rather than
/// being kept alongside: the window's far edge can only move when the household day moves, and that
/// is a rollover crossing.
/// </para>
/// <para>
/// <b>Why the expired anchor is the whole condition, with no record of what was already asked.</b>
/// An earlier shape of this also required the rollover to differ from the last one a refetch had
/// succeeded for, to hold the tick to one fetch per distinct instant. That conflated asking with
/// being answered. The kiosk's clock can sit ahead of the server's — a Pi has no RTC, and the dev
/// clock bridge moves it deliberately — and then a refetch returns the SAME
/// <c>NextRolloverUtc</c>, because the server's own day has not rolled over yet. Recording that as
/// acted on retired the only time-driven path back: the view stayed on yesterday's anchor for the
/// rest of the day, measuring both panels from yesterday, labelling yesterday's rows "Today" and
/// counting yesterday on the badge, until some unrelated fetch happened to rescue it.
/// </para>
/// <para>
/// What stops the asking is the anchor moving into the future, which is a fact already in
/// <paramref name="rolloverUtc"/> — so no bookkeeping is needed to reach it, and a failed fetch
/// retries for free. The cost is that a kiosk whose clock is past the server's anchor refetches once
/// per tick until the server catches up: one small GET a minute, the same cadence as the loop making
/// the decision, and self-limiting because the condition goes false the moment the server answers
/// with an anchor ahead of the kiosk.
/// </para>
/// <para>
/// There is no clock and no zone here. <paramref name="now"/> and
/// <paramref name="effectiveToday"/> are both passed in, which is what makes every case above
/// reachable without driving a component into it.
/// </para>
/// </remarks>
public static class RemindersTickDecision
{
    /// <param name="now">The kiosk's current instant, dev day-offset included.</param>
    /// <param name="rolloverUtc">The instant the server said its anchor expires.</param>
    /// <param name="effectiveToday">The date the view measures against now.</param>
    /// <param name="lastToday">The date it measured against when it last rendered.</param>
    public static RemindersTickOutcome Evaluate(
        DateTimeOffset now,
        DateTimeOffset rolloverUtc,
        DateOnly effectiveToday,
        DateOnly lastToday) =>
        new(
            Refetch: now >= rolloverUtc,
            Render: effectiveToday != lastToday);
}
