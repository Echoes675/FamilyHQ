using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Validators;

public class DisplaySettingDtoValidatorTests
{
    private readonly DisplaySettingDtoValidator _sut = new();

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public async Task SurfaceMultiplier_Valid_PassesValidation(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public async Task SurfaceMultiplier_OutOfRange_FailsValidation(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SurfaceMultiplier");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task TransitionDuration_Valid_PassesValidation(int value)
    {
        var dto = new DisplaySettingDto(0.5, false, value, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task TransitionDuration_OutOfRange_FailsValidation(int value)
    {
        var dto = new DisplaySettingDto(0.5, false, value, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransitionDurationSecs");
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("morning")]
    [InlineData("daytime")]
    [InlineData("evening")]
    [InlineData("night")]
    public async Task ThemeSelection_ValidValue_PassesValidation(string value)
    {
        var dto = new DisplaySettingDto(0.5, false, 15, value);
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("noon")]
    [InlineData("MORNING")]
    public async Task ThemeSelection_InvalidValue_FailsValidation(string value)
    {
        var dto = new DisplaySettingDto(0.5, false, 15, value);
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ThemeSelection");
    }
}
