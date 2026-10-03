// tests/FamilyHQ.WebUi.Tests/Components/Dashboard/IdleSnapDecisionTests.cs
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// An idle kiosk advances to the current day, and returns to its home view, only when nobody has
// touched it for the threshold and no modal is open. Both decisions are pure so they can be
// unit-tested without rendering Index (the project has no bUnit; the timer/JS/snap integration is
// covered by E2E).
public class IdleSnapDecisionTests
{
    private const double Threshold = 900_000; // 15 min in ms
    private static readonly DateOnly Today = new(2026, 6, 9);
    private const DashboardView Home = DashboardView.Reminders;

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

    // ── Returning to the home view ───────────────────────────────────────────

    [Fact]
    public void ShouldReturnToHomeView_WhenAModalIsOpen_DoesNotReturn()
    {
        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: true, idleMs: Threshold + 1,
            thresholdMs: Threshold, currentView: DashboardView.Month, homeView: Home)
            .Should().BeFalse("leaving the view would take an unsaved edit with it");
    }

    [Fact]
    public void ShouldReturnToHomeView_BelowTheIdleThreshold_DoesNotReturn()
    {
        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold - 1,
            thresholdMs: Threshold, currentView: DashboardView.Month, homeView: Home)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldReturnToHomeView_ExactlyAtTheIdleThreshold_Returns()
    {
        // The same boundary ShouldSnap applies (`idleMs < thresholdMs` is the only rejection), which
        // is the whole point of the two rules sharing one constant: a kiosk cannot be idle enough to
        // snap the date and not idle enough to come home.
        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold,
            thresholdMs: Threshold, currentView: DashboardView.Month, homeView: Home)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldReturnToHomeView_WhenAlreadyOnTheHomeView_DoesNotReturn()
    {
        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, currentView: Home, homeView: Home)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(DashboardView.Month)]
    [InlineData(DashboardView.MonthAgenda)]
    [InlineData(DashboardView.Day)]
    public void ShouldReturnToHomeView_WhenIdleOnAnyOtherView_Returns(DashboardView left)
    {
        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, currentView: left, homeView: Home)
            .Should().BeTrue();
    }

    // The two rules are independent, and these are the states that prove it rather than assert it:
    // each one has exactly one of them saying yes. Collapsing them into a single predicate would
    // have to get one of these two wrong.

    [Fact]
    public void OnAnotherViewShowingToday_OnlyTheHomeViewRuleFires()
    {
        var firstOfThisMonth = new DateOnly(2026, 6, 1);

        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: firstOfThisMonth, today: Today, isDayView: false)
            .Should().BeFalse("the month on screen is already today's");

        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, currentView: DashboardView.Month, homeView: Home)
            .Should().BeTrue("nobody has touched it and it is not on the home view");
    }

    [Fact]
    public void OnTheHomeViewWithAStaleMonthLoaded_OnlyTheDateRuleFires()
    {
        var firstOfMay = new DateOnly(2026, 5, 1);

        IdleSnapDecision.ShouldSnap(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, displayedAnchor: firstOfMay, today: Today, isDayView: false)
            .Should().BeTrue("the month the other tabs would open on is stale");

        IdleSnapDecision.ShouldReturnToHomeView(modalOpen: false, idleMs: Threshold + 1,
            thresholdMs: Threshold, currentView: Home, homeView: Home)
            .Should().BeFalse("it is already home");
    }
}
