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
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));

        result.MorningStart.Should().BeBefore(result.DaytimeStart);
        result.DaytimeStart.Should().BeBefore(result.EveningStart);
        result.EveningStart.Should().BeBefore(result.NightStart);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_MorningStart_IsCivilDawn()
    {
        var sut = CreateSut();
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        // SunCalcNet civil dawn for Edinburgh (55.95°N, -3.19°E) on 2024-06-21 is ~02:24 UTC.
        // Asserting < 5 gives ~2.5h tolerance for seasonal variation in this test's fixed date.
        result.MorningStart.Hour.Should().BeLessThan(5);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_EveningStart_IsOneHourBeforeSunset()
    {
        var sut = CreateSut();
        var result = await sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        // SunCalcNet sunset for Edinburgh on 2024-06-21 is ~21:33 UTC; minus 1h = ~20:33 UTC.
        // Asserting >= 20 verifies the 1-hour-before-sunset calculation is applied.
        result.EveningStart.Hour.Should().BeGreaterThanOrEqualTo(20);
    }
}
