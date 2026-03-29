using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class LocationService(ILocationSettingRepository repo, HttpClient httpClient) : ILocationService
{
    public async Task<LocationResult> GetEffectiveLocationAsync(CancellationToken ct = default)
    {
        var saved = await repo.GetAsync(ct);
        if (saved is not null)
            return new LocationResult(saved.PlaceName, saved.Latitude, saved.Longitude, IsAutoDetected: false);

        var response = await httpClient.GetFromJsonAsync<IpApiResponse>(
            "http://ip-api.com/json/?fields=status,city,regionName,country,lat,lon", ct)
            ?? throw new InvalidOperationException("IP geolocation returned null response.");

        var placeName = $"{response.City}, {response.RegionName}, {response.Country}";
        return new LocationResult(placeName, response.Lat, response.Lon, IsAutoDetected: true);
    }

    private record IpApiResponse(string Status, string City, string RegionName, string Country, double Lat, double Lon);
}
