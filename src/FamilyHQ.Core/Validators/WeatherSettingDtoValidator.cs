namespace FamilyHQ.Core.Validators;

using FluentValidation;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;

public class WeatherSettingDtoValidator : AbstractValidator<WeatherSettingDto>
{
    public WeatherSettingDtoValidator()
    {
        RuleFor(x => x.PollIntervalMinutes)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Poll interval must be at least 1 minute.");

        RuleFor(x => x.WindThresholdKmh)
            .GreaterThan(0)
            .WithMessage("Wind threshold must be greater than 0 km/h.");

        RuleFor(x => x.TemperatureUnit)
            .IsInEnum()
            .WithMessage("Temperature unit must be a valid value.");
    }
}
