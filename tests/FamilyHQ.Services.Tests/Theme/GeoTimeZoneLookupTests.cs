using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class GeoTimeZoneLookupTests
{
    private static GeoTimeZoneLookup CreateSut() => new();

    [Fact]
    public void GetTimeZone_LondonCoordinate_ReturnsEuropeLondon()
    {
        // London (51.5074, -0.1278) → Europe/London
        CreateSut().GetTimeZone(51.5074, -0.1278).Should().Be("Europe/London");
    }

    [Fact]
    public void GetTimeZone_DublinCoordinate_ReturnsEuropeDublin()
    {
        // Dublin (53.3498, -6.2603) → Europe/Dublin
        CreateSut().GetTimeZone(53.3498, -6.2603).Should().Be("Europe/Dublin");
    }

    [Fact]
    public void GetTimeZone_TokyoCoordinate_ReturnsAsiaTokyo()
    {
        // Tokyo (35.6762, 139.6503) → Asia/Tokyo
        CreateSut().GetTimeZone(35.6762, 139.6503).Should().Be("Asia/Tokyo");
    }

    [Fact]
    public void GetTimeZone_GulfOfGuineaCoordinate_ReturnsUtc()
    {
        // GeoTimeZone v5.3.0 falls back to UTC/Etc/GMT±N for open-ocean coordinates rather than
        // returning an empty string, so the string.IsNullOrWhiteSpace guard in GeoTimeZoneLookup
        // cannot be exercised through the real library (it is defensive dead code).
        // This test documents the actual behaviour: 0°N 0°E (Gulf of Guinea) → "UTC".
        CreateSut().GetTimeZone(0.0, 0.0).Should().Be("UTC");
    }
}
