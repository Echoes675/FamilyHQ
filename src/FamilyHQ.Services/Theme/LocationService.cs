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
        // `message` must be in the requested fields or ip-api omits it, leaving every failure
        // diagnosed as a bare "fail" (FHQ-114).
        var response = await httpClient.GetFromJsonAsync<IpApiResponse>(
            "json/?fields=status,message,city,regionName,country,lat,lon,timezone", ct)
            ?? throw new InvalidOperationException("IP geolocation returned null response.");

        // FHQ-114: ip-api documents exactly three fail messages — "private range", "reserved range"
        // and "invalid query" — and all three are permanent for the querying IP, so retrying could
        // only return the same answer. Its rate limiting is a separate signal (HTTP 429 + X-Ttl)
        // handled by TransientHttpRetryHandler. A non-success body therefore fails cleanly here.
        if (response.Status != "success")
            throw new InvalidOperationException(
                $"IP geolocation failed with status '{response.Status}' ({response.Message ?? "no reason given"}).");

        var placeName = string.Join(", ", new[] { response.City, response.RegionName, response.Country }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return new LocationResult(placeName, response.Lat, response.Lon, IsAutoDetected: true,
            IanaTimeZone: string.IsNullOrWhiteSpace(response.Timezone) ? null : response.Timezone);
    }

    private record IpApiResponse(string Status, string? Message, string City, string RegionName, string Country, double Lat, double Lon, string? Timezone);
}
