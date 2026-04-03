using FamilyHQ.Core.DTOs;
using FluentValidation;

namespace FamilyHQ.Core.Validators;

public class DisplaySettingDtoValidator : AbstractValidator<DisplaySettingDto>
{
    private static readonly string[] ValidThemeSelections =
        ["auto", "morning", "daytime", "evening", "night"];

    public DisplaySettingDtoValidator()
    {
        RuleFor(x => x.SurfaceMultiplier)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage("Surface multiplier must be between 0.0 and 1.0.");

        RuleFor(x => x.TransitionDurationSecs)
            .InclusiveBetween(0, 60)
            .WithMessage("Transition duration must be between 0 and 60 seconds.");

        RuleFor(x => x.ThemeSelection)
            .NotNull()
            .Must(v => ValidThemeSelections.Contains(v))
            .WithMessage("ThemeSelection must be one of: auto, morning, daytime, evening, night.");
    }
}
