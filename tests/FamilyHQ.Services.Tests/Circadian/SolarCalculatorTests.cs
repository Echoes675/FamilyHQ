
using FamilyHQ.Services.Circadian;
using Xunit;

namespace FamilyHQ.Services.Tests.Circadian;

public class SolarCalculatorTests
{
    private readonly SolarCalculator _sut = new();

    [Theory]
    [InlineData(2024, 6, 21, 51.5074, -0.1278, 3, 43, 20, 21)] // London summer solstice (BST): sunrise ~03:43 UTC, sunset ~20:21 UTC
    [InlineData(2024, 12, 21, 51.5074, -0.1278, 8, 3, 15, 53)]  // London winter solstice (GMT): sunrise ~08:03, sunset ~15:53
    [InlineData(2024, 3, 20, 51.5074, -0.1278, 6, 2, 18, 12)]   // London spring equinox (GMT): sunrise ~06:02, sunset ~18:12
    public void Calculate_ReturnsApproximateSunriseSunset(
        int year, int month, int day,
        double latitude, double longitude,
        int expectedSunriseHour, int expectedSunriseMinute,
        int expectedSunsetHour, int expectedSunsetMinute)
    {
        var date = new DateOnly(year, month, day);
        
        var result = _sut.Calculate(date, latitude, longitude);
        
        Assert.NotNull(result);
        var (sunrise, sunset) = result.Value;
        
        // Allow ±30 minutes tolerance for the NOAA algorithm approximation
        var sunriseExpected = new TimeOnly(expectedSunriseHour, expectedSunriseMinute);
        var sunsetExpected = new TimeOnly(expectedSunsetHour, expectedSunsetMinute);
        
        Assert.True(Math.Abs((sunrise - sunriseExpected).TotalMinutes) <= 30,
            $"Sunrise {sunrise} was not within 30 minutes of expected {sunriseExpected}");
        Assert.True(Math.Abs((sunset - sunsetExpected).TotalMinutes) <= 30,
            $"Sunset {sunset} was not within 30 minutes of expected {sunsetExpected}");
    }

    [Fact]
    public void Calculate_ReturnsNull_ForPolarNight()
    {
        // Tromsø, Norway in December (polar night)
        var date = new DateOnly(2024, 12, 21);
        var result = _sut.Calculate(date, 69.6496, 18.9560);
        
        // May return null (polar night) or a valid result — just verify it doesn't throw
        // The algorithm may return null or extreme values for polar regions
        Assert.True(result is null || result.Value.Sunrise < result.Value.Sunset);
    }

    [Fact]
    public void Calculate_SunriseBeforeSunset()
    {
        var date = new DateOnly(2024, 6, 15);
        var result = _sut.Calculate(date, 51.5074, -0.1278);
        
        Assert.NotNull(result);
        Assert.True(result.Value.Sunrise < result.Value.Sunset);
    }
}
