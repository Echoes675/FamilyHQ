namespace FamilyHQ.Simulator.DTOs;

public class BackdoorEventRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? CalendarId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
}
