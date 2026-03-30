using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Validators;

public class DisplaySettingDtoValidatorTests
{
    private readonly DisplaySettingDtoValidator _sut = new();

    [Fact]
    public void Valid_Defaults_Passes()
    {
        var dto = new DisplaySettingDto(1.0, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void SurfaceMultiplier_OutOfRange_Fails(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SurfaceMultiplier");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void SurfaceMultiplier_AtBoundary_Passes(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void TransitionDurationSecs_OutOfRange_Fails(int value)
    {
        var dto = new DisplaySettingDto(1.0, false, value);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransitionDurationSecs");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public void TransitionDurationSecs_AtBoundary_Passes(int value)
    {
        var dto = new DisplaySettingDto(1.0, false, value);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }
}
