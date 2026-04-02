namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WmoCodeMapperTests
{
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
    public void Maps_wmo_code_to_correct_condition(int wmoCode, WeatherCondition expected)
    {
        var result = WmoCodeMapper.ToCondition(wmoCode);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(50)]
    public void Unknown_wmo_code_returns_clear(int wmoCode)
    {
        var result = WmoCodeMapper.ToCondition(wmoCode);
        result.Should().Be(WeatherCondition.Clear);
    }
}
