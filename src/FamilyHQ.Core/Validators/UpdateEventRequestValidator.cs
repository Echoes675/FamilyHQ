using FluentValidation;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Validators;

public class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Start).NotEmpty().WithMessage("Start time is required.");
        RuleFor(x => x.End).NotEmpty().WithMessage("End time is required.")
            .GreaterThanOrEqualTo(x => x.Start).WithMessage("End time must be after start time.");
    }
}
