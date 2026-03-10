namespace FamilyHQ.Simulator.Models;

public class SimulatorConfigurationModel
{
    public List<CalendarModel> Calendars { get; set; } = new();
    public List<EventModel> Events { get; set; } = new();
    
}