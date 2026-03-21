namespace FamilyHQ.Simulator.Models;

public class SimulatedEventAttendee
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string AttendeeCalendarId { get; set; } = string.Empty;
}
