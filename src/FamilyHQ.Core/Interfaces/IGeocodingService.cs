namespace FamilyHQ.Core.Interfaces;

public interface IGeocodingService
{
    Task<(double Latitude, double Longitude)> GeocodeAsync(string placeName, CancellationToken ct = default);
}
