using FluentValidation;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Validators;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.MemberCalendarInfoIds)
            .NotNull().WithMessage("At least one calendar is required.")
            .Must(ids => ids != null && ids.Count > 0).WithMessage("At least one calendar is required.")
            .Must(ids => ids == null || ids.All(id => id != Guid.Empty)).WithMessage("Calendar ID must not be empty.")
            .Must(ids => ids == null || ids.Distinct().Count() == ids.Count).WithMessage("Duplicate calendar IDs are not allowed.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Start).NotEmpty().WithMessage("Start time is required.");
        RuleFor(x => x.End).NotEmpty().WithMessage("End time is required.")
            .GreaterThanOrEqualTo(x => x.Start).WithMessage("End time must be after start time.");
    }
}
