namespace FamilyHQ.Core.Tests.Enums;

using FamilyHQ.Core.Enums;
using FluentAssertions;

public class WeatherConditionTests
{
    // WeatherDataPoint.Condition is persisted as an integer column, so the ordinal of
    // every existing member is part of the stored data. New members must be APPENDED —
    // inserting one silently re-labels every weather row already in the database.
    [Theory]
    [InlineData(WeatherCondition.Clear, 0)]
    [InlineData(WeatherCondition.PartlyCloudy, 1)]
    [InlineData(WeatherCondition.Cloudy, 2)]
    [InlineData(WeatherCondition.Fog, 3)]
    [InlineData(WeatherCondition.Drizzle, 4)]
    [InlineData(WeatherCondition.LightRain, 5)]
    [InlineData(WeatherCondition.HeavyRain, 6)]
    [InlineData(WeatherCondition.Thunder, 7)]
    [InlineData(WeatherCondition.Snow, 8)]
    [InlineData(WeatherCondition.Sleet, 9)]
    [InlineData(WeatherCondition.Unknown, 10)]
    public void WeatherCondition_HasStablePersistedOrdinal(WeatherCondition condition, int expectedOrdinal)
    {
        ((int)condition).Should().Be(expectedOrdinal);
    }
}
