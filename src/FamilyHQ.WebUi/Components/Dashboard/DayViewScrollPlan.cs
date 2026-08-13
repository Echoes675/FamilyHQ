namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure rule for the Day view scroll-to-now (FHQ-132): whether <c>OnAfterRenderAsync</c> should
/// scroll the grid to the current time, the minutes-of-day target, and the next value of the
/// once-per-day gate. All "today" comparisons use the injected TimeProvider's local now, matching
/// NowLinePosition's kiosk-offset semantics. Extracted so it can be unit-tested with
/// FakeTimeProvider without rendering DayView (the project has no bUnit); the JS scroll itself is
/// covered by E2E.
/// </summary>
public sealed record DayViewScrollPlan(bool ShouldScroll, double TargetMinutesOfDay, DateTime? LastScrolledDate)
{
    /// <param name="selectedDate">The date the Day view is showing.</param>
    /// <param name="lastScrolledDate">The once-per-day gate: the day the view last auto-scrolled on, or null.</param>
    /// <param name="clock">Kiosk-adjusted clock; "today" and "now" are its local values.</param>
    public static DayViewScrollPlan Decide(DateTime selectedDate, DateTime? lastScrolledDate, TimeProvider clock)
    {
        var now = clock.GetLocalNow();
        var today = now.Date;

        if (selectedDate.Date != today)
        {
            return new DayViewScrollPlan(false, 0, null);
        }

        return lastScrolledDate == today
            ? new DayViewScrollPlan(false, 0, today)
            : new DayViewScrollPlan(true, now.TimeOfDay.TotalMinutes, today);
    }
}
