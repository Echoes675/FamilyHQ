using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class LocationService(HttpClient httpClient) : ILocationService
{
    // Resolves a location via IP auto-detection only.  Per-user saved locations
    // must be looked up via ILocationSettingRepository.GetAsync(userId, ct) by
    // the caller — including any saved-location behaviour here would require an
    // unfiltered repo call that leaks across users in parallel-test scenarios.
    public async Task<LocationResult> GetEffectiveLocationAsync(CancellationToken ct = default)
    {
        var response = await httpClient.GetFromJsonAsync<IpApiResponse>(
            "json/?fields=status,city,regionName,country,lat,lon", ct)
            ?? throw new InvalidOperationException("IP geolocation returned null response.");

        if (response.Status != "success")
            throw new InvalidOperationException($"IP geolocation failed with status '{response.Status}'.");

        var placeName = string.Join(", ", new[] { response.City, response.RegionName, response.Country }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return new LocationResult(placeName, response.Lat, response.Lon, IsAutoDetected: true);
    }

    private record IpApiResponse(string Status, string City, string RegionName, string Country, double Lat, double Lon);
}
