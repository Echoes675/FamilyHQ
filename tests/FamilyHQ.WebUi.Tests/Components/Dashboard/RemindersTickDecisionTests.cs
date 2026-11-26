using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// What one minute's tick should do. A pure function rather than logic inside the component, for this
// repo's usual reason — there is no bUnit here, so a decision a `.razor` file makes is a decision
// nothing can assert on — and because the cases that matter are all about state the component would
// otherwise have to be driven into: a refetch that did not advance the anchor, a refetch that failed,
// a kiosk whose day was shifted by the dev bridge.
public class RemindersTickDecisionTests
{
    private static readonly DateTimeOffset Rollover = new(2026, 6, 16, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public void Evaluate_BeforeTheRollover_DoesNothing() =>
        RemindersTickDecision.Evaluate(Rollover.AddMinutes(-1), Rollover, Today, lastToday: Today)
            .Should().Be(new RemindersTickOutcome(Refetch: false, Render: false));

    [Fact]
    public void Evaluate_AtTheRolloverInstant_Refetches() =>
        // Inclusive: the rollover IS the first instant the anchor is stale.
        RemindersTickDecision.Evaluate(Rollover, Rollover, Today, lastToday: Today)
            .Refetch.Should().BeTrue();

    [Fact]
    public void Evaluate_WhenARefetchCameBackWithTheSameRollover_StillRefetchesOnTheNextTick() =>
        // The kiosk's clock can sit ahead of the server's — a Pi has no RTC, and the dev clock bridge
        // moves it deliberately — and then a refetch returns the SAME anchor, because the server's own
        // day has not rolled over yet. Asking is not the same as being answered: a tick that recorded
        // "I already asked for this instant" would never ask again, and the view would spend the rest
        // of the day measuring both panels from yesterday, labelling yesterday's rows "Today" and
        // counting yesterday on the badge, until some unrelated fetch happened to rescue it. A refetch
        // that THREW is the same input state and retries for the same reason.
        RemindersTickDecision.Evaluate(Rollover.AddMinutes(2), Rollover, Today, lastToday: Today)
            .Refetch.Should().BeTrue();

    [Fact]
    public void Evaluate_OnceTheRefetchedAnchorIsInTheFuture_StopsRefetching() =>
        // The actual brake, and the reason no record of what was asked is needed to have one: the
        // server answered with an anchor ahead of the kiosk, so there is nothing stale left to chase
        // and the per-minute tick goes quiet on its own.
        RemindersTickDecision.Evaluate(Rollover.AddMinutes(1), Rollover.AddDays(1), Today, lastToday: Today)
            .Refetch.Should().BeFalse();

    [Fact]
    public void Evaluate_WhenTheEffectiveDateHasMoved_Renders() =>
        // The day is the WHOLE render condition, as it was before: a month cannot change without the
        // day changing, and every other path that refreshes the rows renders for itself. Rendering
        // unconditionally would diff the whole month grid every minute on a Pi for no visible change.
        RemindersTickDecision.Evaluate(
                Rollover.AddMinutes(-1), Rollover, effectiveToday: Today.AddDays(1), lastToday: Today)
            .Should().Be(new RemindersTickOutcome(Refetch: false, Render: true));

    [Fact]
    public void Evaluate_WhenTheDevBridgeMovesTheKiosksDayPastTheRollover_BothRefetchesAndRenders() =>
        // A clock jump does two things at once, and they are independent: the date on screen has
        // moved (render) and the anchor the server gave is behind the kiosk (refetch).
        RemindersTickDecision.Evaluate(
                Rollover.AddDays(1), Rollover, effectiveToday: Today.AddDays(1), lastToday: Today)
            .Should().Be(new RemindersTickOutcome(Refetch: true, Render: true));
}
