namespace FamilyHQ.Simulator.Models;

public class SimulatedWatchChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CalendarId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}
