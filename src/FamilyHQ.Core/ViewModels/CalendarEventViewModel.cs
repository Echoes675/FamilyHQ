namespace FamilyHQ.Core.ViewModels;

public class CalendarEventViewModel
{
    public Guid Id { get; set; }
    public Guid CalendarInfoId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? CalendarColor { get; set; }
}
