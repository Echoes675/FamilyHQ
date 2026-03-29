namespace FamilyHQ.Core.DTOs;

public record LocationResult(
    string PlaceName,
    double Latitude,
    double Longitude,
    bool IsAutoDetected
);
