namespace FamilyHQ.Core.Models;

using FamilyHQ.Core.Enums;

public class WeatherSetting
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 30;
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public double WindThresholdKmh { get; set; } = 30;
    public string? ApiKey { get; set; }
}
