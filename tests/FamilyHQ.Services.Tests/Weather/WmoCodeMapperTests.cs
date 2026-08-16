namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WmoCodeMapperTests
{
    private static WmoCodeMapper CreateSut() => new();

    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.PartlyCloudy)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Cloudy)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(48, WeatherCondition.Fog)]
    [InlineData(51, WeatherCondition.Drizzle)]
    [InlineData(53, WeatherCondition.Drizzle)]
    [InlineData(55, WeatherCondition.Drizzle)]
    [InlineData(56, WeatherCondition.Sleet)]
    [InlineData(57, WeatherCondition.Sleet)]
    [InlineData(61, WeatherCondition.LightRain)]
    [InlineData(63, WeatherCondition.HeavyRain)]
    [InlineData(65, WeatherCondition.HeavyRain)]
    [InlineData(66, WeatherCondition.Sleet)]
    [InlineData(67, WeatherCondition.Sleet)]
    [InlineData(71, WeatherCondition.Snow)]
    [InlineData(73, WeatherCondition.Snow)]
    [InlineData(75, WeatherCondition.Snow)]
    [InlineData(77, WeatherCondition.Snow)]
    [InlineData(80, WeatherCondition.LightRain)]
    [InlineData(81, WeatherCondition.HeavyRain)]
    [InlineData(82, WeatherCondition.HeavyRain)]
    [InlineData(85, WeatherCondition.Snow)]
    [InlineData(86, WeatherCondition.Snow)]
    [InlineData(95, WeatherCondition.Thunder)]
    [InlineData(96, WeatherCondition.Thunder)]
    [InlineData(99, WeatherCondition.Thunder)]
    public void TryGetCondition_MappedWmoCode_ReturnsTrueWithCorrectCondition(int wmoCode, WeatherCondition expected)
    {
        var mapped = CreateSut().TryGetCondition(wmoCode, out var condition);

        mapped.Should().BeTrue();
        condition.Should().Be(expected);
    }

    // FHQ-115: an unmapped code used to fall through to Clear, so an unrecognised
    // severe-weather code rendered as sunny on the family dashboard.
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(50)]
    [InlineData(100)]
    public void TryGetCondition_UnmappedWmoCode_ReturnsFalseWithUnknown(int wmoCode)
    {
        var mapped = CreateSut().TryGetCondition(wmoCode, out var condition);

        mapped.Should().BeFalse();
        condition.Should().Be(WeatherCondition.Unknown,
            "an unrecognised code must never be presented as clear skies");
    }
}
