namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The pure rules governing an idle kiosk: whether to pull the displayed DATE back to today, and
/// whether to return to the dashboard's home VIEW. Extracted so they can be unit-tested without
/// rendering Index — the project has no bUnit, and the timer/JS/snap integration is covered by E2E.
/// </summary>
/// <remarks>
/// Two independent conditions rather than one predicate with an extra clause, because on the same
/// observed state they disagree in both directions: a kiosk left on the Month view showing the
/// current month needs no date snap and still has to come home, and one already on the home view
/// with a stale month loaded needs the snap and no view change. Callers evaluate both against the
/// same snapshot and apply whichever say yes — either, both, or neither.
/// </remarks>
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

    /// <summary>
    /// Whether an idle dashboard should return to its home view. On a wall display nobody navigates
    /// home — whatever the last person left on screen stays there until somebody touches it — so
    /// coming back is the kiosk's own job.
    /// </summary>
    /// <param name="modalOpen">
    /// Any add/edit/quick-jump/day-picker modal is open. Gated on for the same reason the date rule
    /// gates on it, and a stronger one: leaving the view would take an unsaved edit with it.
    /// </param>
    /// <param name="idleMs">Milliseconds since the last user interaction (monotonic).</param>
    /// <param name="thresholdMs">
    /// Idle threshold before a return is allowed. The same value the date rule uses — the family
    /// settled on one definition of "nobody is at it", not two.
    /// </param>
    /// <param name="currentView">The view on screen now.</param>
    /// <param name="homeView">The view a page load lands on, and the one an idle kiosk returns to.</param>
    public static bool ShouldReturnToHomeView(
        bool modalOpen, double idleMs, double thresholdMs,
        DashboardView currentView, DashboardView homeView)
    {
        if (modalOpen) return false;
        if (idleMs < thresholdMs) return false;
        return currentView != homeView;
    }

    private static bool IsShowingToday(DateOnly anchor, DateOnly today, bool isDayView) =>
        isDayView
            ? anchor == today
            : anchor.Year == today.Year && anchor.Month == today.Month;
}
