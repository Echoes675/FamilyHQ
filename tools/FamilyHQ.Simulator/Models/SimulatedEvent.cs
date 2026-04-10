namespace FamilyHQ.Simulator.Models;

public class SimulatedEvent
{
    public string Id { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? ContentHash { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? UserId { get; set; }
    public bool IsDeleted { get; set; }
}