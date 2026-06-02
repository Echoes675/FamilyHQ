using FluentAssertions;
using GeoTimeZone;
using NodaTime;
using NodaTime.Text;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

// FHQ-43: proves the bundled-data tz stack works in the globalization-invariant CI container
// (no ICU / OS tzdata / TimeZoneInfo). If this is red in Jenkins, stop and reassess the approach.
public class TimeZoneSpikeTests
{
    private static readonly LocalDateTimePattern Pattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm:ss");

    [Fact]
    public void GeoTimeZone_ResolvesLondonLatLon_ToEuropeLondon()
    {
        var result = TimeZoneLookup.GetTimeZone(51.5074, -0.1278).Result;
        result.Should().Be("Europe/London");
    }

    [Fact]
    public void NodaTime_HoldsLocalWallClock_AcrossDst()
    {
        var zone = DateTimeZoneProviders.Tzdb["Europe/London"];

        var summer = Instant.FromUtc(2026, 7, 1, 8, 0).InZone(zone).LocalDateTime;
        Pattern.Format(summer).Should().Be("2026-07-01T09:00:00");

        var winter = Instant.FromUtc(2026, 1, 1, 9, 0).InZone(zone).LocalDateTime;
        Pattern.Format(winter).Should().Be("2026-01-01T09:00:00");
    }
}
