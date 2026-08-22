namespace FamilyHQ.Simulator.Models;

public class CalendarModel
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? BackgroundColor { get; set; }
    public bool IsShared { get; set; } = false;

    /// <summary>
    /// FHQ-164: the calendar's own default IANA zone (Google's calendar-resource <c>timeZone</c>),
    /// e.g. "Europe/London" — the zone Google applies to an event on this calendar that carries none
    /// of its own, and the last Google-supplied rung of the app's series-zone discovery ladder. Null
    /// seeds a calendar that reports no zone.
    /// </summary>
    public string? TimeZone { get; set; }
}