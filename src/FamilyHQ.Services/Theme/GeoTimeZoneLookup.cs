using FamilyHQ.Core.Interfaces;
using GeoTimeZone;

namespace FamilyHQ.Services.Theme;

public class GeoTimeZoneLookup : ITimeZoneLookup
{
    public string? GetTimeZone(double latitude, double longitude)
    {
        var result = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
