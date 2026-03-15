using System.Collections.Generic;

namespace FamilyHQ.E2E.Data.Models;

public class SimulatorConfigurationModel
{
    public string UserName { get; set; } = string.Empty;
    public List<SimulatorCalendarModel> Calendars { get; set; } = new();
    public List<SimulatorEventModel> Events { get; set; } = new();
}
