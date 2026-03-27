namespace FamilyHQ.Services.Options;

public class KioskOptions
{
    public const string SectionName = "Kiosk";
    
    /// <summary>Latitude of the kiosk location in decimal degrees (positive = North)</summary>
    public double Latitude { get; set; } = 51.5074; // Default: London
    
    /// <summary>Longitude of the kiosk location in decimal degrees (positive = East)</summary>
    public double Longitude { get; set; } = -0.1278; // Default: London
}
