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

        // Only when the request actually carries reminders. A request without them is not a reminder
        // change at all, so none of the reminder rules may fire on it.
        RuleFor(x => x.Reminders!)
            .SetValidator(new EventRemindersValidator())
            .When(x => x.Reminders is not null);
    }
}
