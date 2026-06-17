using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class GeoTimeZoneLookupTests
{
    private static GeoTimeZoneLookup CreateSut() => new();

    [Fact]
    public void GetTimeZone_KnownCoordinate_ReturnsCorrectIanaZone()
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
}
