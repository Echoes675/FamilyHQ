namespace FamilyHQ.Core.DTOs;

public record MonthViewDto(
    int Year,
    int Month,
    Dictionary<int, List<CalendarEventDto>> EventsByDay);
