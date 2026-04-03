namespace FamilyHQ.Core.Tests.Validators;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Validators;
using FluentAssertions;

public class WeatherSettingDtoValidatorTests
{
    private readonly WeatherSettingDtoValidator _sut = new();

    [Fact]
    public void Valid_settings_pass_validation()
    {
        var dto = new WeatherSettingDto(true, 30, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Poll_interval_below_minimum_fails(int interval)
    {
        var dto = new WeatherSettingDto(true, interval, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PollIntervalMinutes");
    }

    [Fact]
    public void Poll_interval_of_one_passes()
    {
        var dto = new WeatherSettingDto(true, 1, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Wind_threshold_zero_or_negative_fails(double threshold)
    {
        var dto = new WeatherSettingDto(true, 30, TemperatureUnit.Celsius, threshold, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "WindThresholdKmh");
    }

    [Fact]
    public void Invalid_temperature_unit_fails()
    {
        var dto = new WeatherSettingDto(true, 30, (TemperatureUnit)99, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TemperatureUnit");
    }
}
