namespace FamilyHQ.Simulator.Models;

public class SimulatedWeather
{
    public int Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DataType { get; set; } = "current"; // "current", "hourly", "daily"
    public string Time { get; set; } = "";
    public int WeatherCode { get; set; }
    public double Temperature { get; set; }
    public double? TemperatureMax { get; set; }
    public double? TemperatureMin { get; set; }
    public double WindSpeed { get; set; }
    public double? WindSpeedMax { get; set; }
}
