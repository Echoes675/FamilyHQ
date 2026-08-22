namespace FamilyHQ.Simulator.Models;

public class SimulatedCalendar
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public string BackgroundColor { get; set; } = "#9e9e9e";
    public string? UserId { get; set; }

    // FHQ-164: the calendar resource's own default IANA zone, which Google returns on every
    // calendarList entry and applies to any event on the calendar that carries no zone of its own.
    // It is the last Google-supplied rung of the app's series-zone discovery ladder, so a Simulator
    // that never reports one leaves that rung unexercised everywhere. Null = not configured.
    public string? TimeZone { get; set; }
}
