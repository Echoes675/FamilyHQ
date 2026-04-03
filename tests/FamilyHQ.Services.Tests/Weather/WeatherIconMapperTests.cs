namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WeatherIconMapperTests
{
    [Theory]
    [InlineData(WeatherCondition.Clear, "clear")]
    [InlineData(WeatherCondition.PartlyCloudy, "partly-cloudy")]
    [InlineData(WeatherCondition.Cloudy, "cloudy")]
    [InlineData(WeatherCondition.Fog, "fog")]
    [InlineData(WeatherCondition.Drizzle, "drizzle")]
    [InlineData(WeatherCondition.LightRain, "light-rain")]
    [InlineData(WeatherCondition.HeavyRain, "heavy-rain")]
    [InlineData(WeatherCondition.Thunder, "thunder")]
    [InlineData(WeatherCondition.Snow, "snow")]
    [InlineData(WeatherCondition.Sleet, "sleet")]
    public void Maps_condition_to_icon_name(WeatherCondition condition, string expectedIcon)
    {
        var result = WeatherIconMapper.ToIconName(condition);
        result.Should().Be(expectedIcon);
    }
}
