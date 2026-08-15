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
    [InlineData(WeatherCondition.Unknown, "unknown")]
    public void Maps_condition_to_icon_name(WeatherCondition condition, string expectedIcon)
    {
        var result = WeatherIconMapper.ToIconName(condition);
        result.Should().Be(expectedIcon);
    }

    // FHQ-115: the fallback arm used to return "clear", so any condition the mapper did
    // not know about drew a sun on the dashboard.
    [Fact]
    public void ToIconName_ConditionOutsideTheEnum_ReturnsUnknownIcon()
    {
        var result = WeatherIconMapper.ToIconName((WeatherCondition)999);

        result.Should().Be("unknown");
    }

    [Fact]
    public void ToIconName_EveryDeclaredCondition_HasItsOwnIconName()
    {
        var conditions = Enum.GetValues<WeatherCondition>();

        var iconNames = conditions.Select(WeatherIconMapper.ToIconName).ToList();

        iconNames.Should().OnlyHaveUniqueItems(
            "a new WeatherCondition that falls through to the unknown arm would silently render as another condition's icon");
    }
}
