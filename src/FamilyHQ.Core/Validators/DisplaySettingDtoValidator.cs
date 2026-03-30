using FamilyHQ.Core.DTOs;
using FluentValidation;

namespace FamilyHQ.Core.Validators;

public class DisplaySettingDtoValidator : AbstractValidator<DisplaySettingDto>
{
    public DisplaySettingDtoValidator()
    {
        RuleFor(x => x.SurfaceMultiplier)
            .InclusiveBetween(0.0, 2.0)
            .WithMessage("Surface multiplier must be between 0.0 and 2.0.");

        RuleFor(x => x.TransitionDurationSecs)
            .InclusiveBetween(0, 60)
            .WithMessage("Transition duration must be between 0 and 60 seconds.");
    }
}
