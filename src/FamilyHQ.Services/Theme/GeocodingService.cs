using System.Net.Http.Json;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class GeocodingService(HttpClient httpClient) : IGeocodingService
{
    public async Task<(double Latitude, double Longitude)> GeocodeAsync(string placeName, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(placeName);
        var url = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=1";

        var results = await httpClient.GetFromJsonAsync<NominatimResult[]>(url, ct);
        if (results is null || results.Length == 0)
            throw new InvalidOperationException($"No geocoding results found for '{placeName}'.");

        return (double.Parse(results[0].Lat, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(results[0].Lon, System.Globalization.CultureInfo.InvariantCulture));
    }

    private record NominatimResult(string Lat, string Lon);
}
