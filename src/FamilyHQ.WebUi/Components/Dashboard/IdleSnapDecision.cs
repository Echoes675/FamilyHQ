namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure rule for the kiosk day-rollover (FHQ-63): decide whether an idle dashboard should
/// snap to the current day. Extracted so it can be unit-tested without rendering Index — the
/// project has no bUnit, and the timer/JS/snap integration is covered by E2E.
/// </summary>
public static class IdleSnapDecision
{
    /// <param name="modalOpen">Any add/edit/quick-jump/day-picker modal is open.</param>
    /// <param name="idleMs">Milliseconds since the last user interaction (monotonic).</param>
    /// <param name="thresholdMs">Idle threshold before a snap is allowed.</param>
    /// <param name="displayedAnchor">Day view: the selected date. Month/Agenda: the displayed month (any day in it).</param>
    /// <param name="today">The current local date, resolved at call time.</param>
    /// <param name="isDayView">True for Day view (compares exact date); false for Month/Agenda (compares year+month).</param>
    public static bool ShouldSnap(
        bool modalOpen, double idleMs, double thresholdMs,
        DateOnly displayedAnchor, DateOnly today, bool isDayView)
    {
        if (modalOpen) return false;
        if (idleMs < thresholdMs) return false;
        return !IsShowingToday(displayedAnchor, today, isDayView);
    }

    private static bool IsShowingToday(DateOnly anchor, DateOnly today, bool isDayView) =>
        isDayView
            ? anchor == today
            : anchor.Year == today.Year && anchor.Month == today.Month;
}
