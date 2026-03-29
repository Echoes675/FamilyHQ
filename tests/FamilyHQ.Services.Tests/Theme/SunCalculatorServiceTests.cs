using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class SunCalculatorServiceTests
{
    private readonly SunCalculatorService _sut = new();

    [Fact]
    public async Task CalculateBoundariesAsync_ReturnsCorrectOrder_ForKnownLocation()
    {
        // Edinburgh, 2024-06-21 (summer solstice — all four periods expected)
        var result = await _sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));

        result.MorningStart.Should().BeBefore(result.DaytimeStart);
        result.DaytimeStart.Should().BeBefore(result.EveningStart);
        result.EveningStart.Should().BeBefore(result.NightStart);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_MorningStart_IsCivilDawn()
    {
        // Edinburgh summer — civil dawn expected well before 5am
        var result = await _sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        result.MorningStart.Hour.Should().BeLessThan(5);
    }

    [Fact]
    public async Task CalculateBoundariesAsync_EveningStart_IsOneHourBeforeSunset()
    {
        // Edinburgh summer — sunset roughly 21:30, so evening ~20:30
        var result = await _sut.CalculateBoundariesAsync(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        result.EveningStart.Hour.Should().BeGreaterThanOrEqualTo(20);
    }
}
