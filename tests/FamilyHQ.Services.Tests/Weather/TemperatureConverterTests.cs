namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class TemperatureConverterTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(20, 68)]
    public void Converts_celsius_to_fahrenheit(double celsius, double expectedFahrenheit)
    {
        var result = TemperatureConverter.Convert(celsius, TemperatureUnit.Fahrenheit);
        result.Should().BeApproximately(expectedFahrenheit, 0.1);
    }

    [Fact]
    public void Celsius_unit_returns_unchanged()
    {
        var result = TemperatureConverter.Convert(20, TemperatureUnit.Celsius);
        result.Should().Be(20);
    }
}
