namespace FamilyHQ.Core.Models;

public class DayTheme
{
    public int Id { get; set; }

    /// <summary>
    /// The kiosk this theme belongs to. Boundaries are derived from that kiosk's saved
    /// <see cref="LocationSetting"/>, so two kiosks in different places get different rows for the
    /// same date (FHQ-177).
    /// </summary>
    public string UserId { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly MorningStart { get; set; }
    public TimeOnly DaytimeStart { get; set; }
    public TimeOnly EveningStart { get; set; }
    public TimeOnly NightStart { get; set; }
    public string? IanaTimeZone { get; set; }
}
