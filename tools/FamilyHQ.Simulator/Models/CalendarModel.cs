namespace FamilyHQ.Simulator.Models;

public class CalendarModel
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? BackgroundColor { get; set; }
    public bool IsShared { get; set; } = false;
}