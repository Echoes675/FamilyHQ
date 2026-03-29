namespace FamilyHQ.Core.Models;

public class DayTheme
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly MorningStart { get; set; }
    public TimeOnly DaytimeStart { get; set; }
    public TimeOnly EveningStart { get; set; }
    public TimeOnly NightStart { get; set; }
}
