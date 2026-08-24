using System.Net.Http.Json;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class GeocodingService(HttpClient httpClient) : IGeocodingService
{
    public async Task<(double Latitude, double Longitude)> GeocodeAsync(string placeName, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(placeName);
        var url = $"search?q={encoded}&format=json&limit=1";

        var results = await httpClient.GetFromJsonAsync<NominatimResult[]>(url, ct);
        if (results is null || results.Length == 0)
            // FHQ-166: the place name stays out of the message. This exception reaches an
            // ILogger wherever it is not caught, which is the same sink as a log template, and the
            // searched-for address is the family's home. The caller (SettingsController) already
            // knows what the user typed and turns this into a "check the spelling" 400.
            throw new InvalidOperationException("No geocoding results found for the requested place name.");

        return (double.Parse(results[0].Lat, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(results[0].Lon, System.Globalization.CultureInfo.InvariantCulture));
    }

    private record NominatimResult(string Lat, string Lon);
}
