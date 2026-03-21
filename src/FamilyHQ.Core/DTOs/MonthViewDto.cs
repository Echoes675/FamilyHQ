namespace FamilyHQ.Core.DTOs;

public class MonthViewDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Dictionary<string, List<CalendarEventDto>> Days { get; set; } = new();
}
