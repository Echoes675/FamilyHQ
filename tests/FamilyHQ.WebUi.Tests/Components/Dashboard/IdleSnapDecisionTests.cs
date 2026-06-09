// tests/FamilyHQ.WebUi.Tests/Components/Dashboard/IdleSnapDecisionTests.cs
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-63: the kiosk auto-advances to the current day only when idle and no modal is open.
// The decision is pure so it can be unit-tested without rendering Index (the project has no
// bUnit; the timer/JS/snap integration is covered by E2E).
public class IdleSnapDecisionTests
{
    private const double Threshold = 900_000; // 15 min in ms
    private static readonly DateOnly Today = new(2026, 6, 9);

    [Fact]
    public void DoesNotSnap_WhenModalOpen_EvenIfIdleAndStale()
    {
        var stale = new DateOnly(2026, 6, 8);
        IdleSnapDecision.ShouldSnap(modalOpen: true, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: stale, today: Today, isDayView: true)
            .Should().BeFalse();
    }

    [Fact]
    public void DoesNotSnap_WhenBelowIdleThreshold()
    {
        var stale = new DateOnly(2026, 6, 8);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold - 1,
            thresholdMs: Threshold, displayedAnchor: stale, today: Today, isDayView: true)
            .Should().BeFalse();
    }

    [Fact]
    public void DayView_DoesNotSnap_WhenAlreadyShowingToday()
    {
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: Today, today: Today, isDayView: true)
            .Should().BeFalse();
    }

    [Fact]
    public void DayView_Snaps_WhenIdleAndShowingPastDay()
    {
        var yesterday = new DateOnly(2026, 6, 8);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: yesterday, today: Today, isDayView: true)
            .Should().BeTrue();
    }

    [Fact]
    public void DayView_Snaps_WhenIdleAndShowingFutureDay()
    {
        var future = new DateOnly(2026, 6, 13);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: future, today: Today, isDayView: true)
            .Should().BeTrue();
    }

    [Fact]
    public void MonthView_DoesNotSnap_WhenAnchorIsInTodaysMonth()
    {
        var firstOfThisMonth = new DateOnly(2026, 6, 1);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: firstOfThisMonth, today: Today, isDayView: false)
            .Should().BeFalse();
    }

    [Fact]
    public void MonthView_Snaps_WhenAnchorIsAPreviousMonth()
    {
        var firstOfMay = new DateOnly(2026, 5, 1);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: firstOfMay, today: Today, isDayView: false)
            .Should().BeTrue();
    }

    [Fact]
    public void MonthView_Snaps_AcrossYearBoundary()
    {
        var dec = new DateOnly(2025, 12, 1);
        var jan = new DateOnly(2026, 1, 5);
        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: dec, today: jan, isDayView: false)
            .Should().BeTrue();
    }
}
