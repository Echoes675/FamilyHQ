namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure rules for the Day view "now" indicator (FHQ-127): whether the line renders for the
/// selected date and where it sits vertically in the 24-hour grid. Extracted so it can be
/// unit-tested without rendering DayView (the project has no bUnit); the per-minute refresh
/// loop in DayView mirrors HeaderClock and is covered by E2E.
/// </summary>
public static class NowLinePosition
{
    private const double MinutesPerDay = 1440.0;

    /// <summary>The line renders only when the selected date is the current local day.</summary>
    public static bool IsVisible(DateTime selectedDate, TimeProvider clock)
        => selectedDate.Date == clock.GetLocalNow().Date;

    /// <summary>Vertical offset of the line as a percentage of the 24-hour grid height.</summary>
    public static double TopPercent(TimeProvider clock)
        => clock.GetLocalNow().TimeOfDay.TotalMinutes / MinutesPerDay * 100;
}
