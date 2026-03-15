namespace FamilyHQ.Simulator.Models;

public class SimulatorConfigurationModel
{
    public string UserName { get; set; } = string.Empty;
    public List<CalendarModel> Calendars { get; set; } = new();
    public List<EventModel> Events { get; set; } = new();
}