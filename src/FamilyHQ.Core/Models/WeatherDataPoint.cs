namespace FamilyHQ.Core.Models;

using FamilyHQ.Core.Enums;

public class WeatherDataPoint
{
    public int Id { get; set; }
    public int LocationSettingId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public WeatherCondition Condition { get; set; }
    public double TemperatureCelsius { get; set; }
    public double? HighCelsius { get; set; }
    public double? LowCelsius { get; set; }
    public double WindSpeedKmh { get; set; }
    public bool IsWindy { get; set; }
    public WeatherDataType DataType { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }
}
