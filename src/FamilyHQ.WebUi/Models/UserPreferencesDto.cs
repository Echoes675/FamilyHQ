namespace FamilyHQ.WebUi.Models;

public class UserPreferencesDto
{
    public int EventDensity { get; set; } = 2;
    public string? CalendarColumnOrder { get; set; }
    public string? CalendarColorOverrides { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}