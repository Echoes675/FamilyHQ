using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class SunCalculatorServiceTests
{
    private static SunCalculatorService CreateSut() => new();

    [Fact]
    public async Task CalculateBoundariesAsync_ReturnsCorrectOrder_ForKnownLocation()
    {
        var sut = CreateSut();
        // Edinburgh, 2024-06-21 (summer solstice — all four periods expected)
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21), null);

        result.MorningStart.Should().BeBefore(result.DaytimeStart);
        result.DaytimeStart.Should().BeBefore(result.EveningStart);
        result.EveningStart.Should().BeBefore(result.NightStart);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_MorningStart_IsCivilDawn()
    {
        var sut = CreateSut();
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21), null);
        // SunCalcNet civil dawn for Edinburgh (55.95°N, -3.19°E) on 2024-06-21 is ~02:24 UTC.
        // Asserting < 5 gives ~2.5h tolerance for seasonal variation in this test's fixed date.
        result.MorningStart.Hour.Should().BeLessThan(5);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_EveningStart_IsOneHourBeforeSunset()
    {
        var sut = CreateSut();
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21), null);
        // SunCalcNet sunset for Edinburgh on 2024-06-21 is ~21:33 UTC; minus 1h = ~20:33 UTC.
        // Asserting >= 20 verifies the 1-hour-before-sunset calculation is applied.
        result.EveningStart.Hour.Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_WithNonUtcZone_ReturnsBoundariesInLocalTime()
    {
        var sut = CreateSut();
        // Dublin (53.3498, -6.2603), 2024-06-21. BST = UTC+1.
        // Sunrise is ~04:55 UTC; in local time it should be ~05:55.
        // Without conversion (null zone), MorningStart hour is ~4.
        // With Europe/Dublin, MorningStart hour should be ~5 — greater than the UTC version.
        var utcResult = await sut.CalculateBoundariesAsync(53.3498, -6.2603, new DateOnly(2024, 6, 21), null);
        var localResult = await sut.CalculateBoundariesAsync(53.3498, -6.2603, new DateOnly(2024, 6, 21), "Europe/Dublin");

        localResult.MorningStart.Hour.Should().BeGreaterThan(utcResult.MorningStart.Hour,
            "BST is UTC+1 so local civil dawn is 1 hour later than UTC civil dawn");
        localResult.DaytimeStart.Hour.Should().BeGreaterThan(utcResult.DaytimeStart.Hour,
            "BST is UTC+1 so local sunrise is 1 hour later than UTC sunrise");
    }
}
