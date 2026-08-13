namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure rule for the dashboard header clock text (FHQ-131): the displayed time is a function
/// of the injected <see cref="TimeProvider"/> (the app-wide <c>KioskTimeProvider</c>), never
/// <c>DateTime.Now</c>, so a simulated/offset kiosk time agrees with the rest of the UI.
/// Extracted so it can be unit-tested with FakeTimeProvider (no bUnit in this project).
/// </summary>
public static class HeaderClockDisplay
{
    private const string TimeFormat = "HH:mm";

    /// <summary>The header clock string ("HH:mm") for the clock's current local time.</summary>
    public static string CurrentTime(TimeProvider clock) => clock.GetLocalNow().ToString(TimeFormat);
}
