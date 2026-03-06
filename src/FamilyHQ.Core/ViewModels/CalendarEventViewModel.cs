namespace FamilyHQ.Core.ViewModels;

public class CalendarEventViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }
    public string? CalendarName { get; set; }
    public string? CalendarColor { get; set; }
}
