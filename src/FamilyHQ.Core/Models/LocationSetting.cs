namespace FamilyHQ.Core.Models;

public class LocationSetting
{
    public int Id { get; set; }
    public string PlaceName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
