using FluentValidation;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Validators;

/// <summary>
/// Validates a reminder set the <b>kiosk</b> is asking to write. Lives in FamilyHQ.Core so the same
/// rules run in the browser and on the server.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only validation there is, and it applies to kiosk-created values only.</b> Google
/// accepts nearly everything with a <c>200</c> and then silently rewrites it: a negative
/// <c>minutes</c> becomes <c>0</c>, anything above the ceiling is clamped down to it, duplicates are
/// collapsed, and a method it does not recognise is dropped outright — leaving the event with no
/// reminder while the request looked successful. Only a sixth override is actually rejected. So the
/// ceilings below mirror what Google enforces in its own editor, and refusing a value here is what
/// stops the family being shown a reminder Google never stored.
/// </para>
/// <para>
/// Values Google <i>supplied</i> are never routed through this validator. Google is the authority on
/// its own data: a <c>method</c> or a <c>minutes</c> the kiosk could not have created must round-trip
/// untouched, and "correcting" it on the way back would be the same class of bug as overwriting an
/// event's time zone with a local setting.
/// </para>
/// </remarks>
public class EventRemindersValidator : AbstractValidator<EventReminders>
{
    /// <summary>Google rejects the sixth override outright (<c>eventRemindersCountExceedsLimit</c>).</summary>
    public const int MaxOverrides = 5;

    /// <summary>A reminder at the event's start. Google clamps anything below this to it.</summary>
    public const int MinMinutes = 0;

    /// <summary>Four weeks, the furthest ahead Google will store a reminder. Anything above is clamped down.</summary>
    public const int MaxMinutes = 40320;

    /// <summary>A notification on the device. Google's value, case-sensitive.</summary>
    public const string PopupMethod = "popup";

    /// <summary>An email to the calendar's account. Google's value, case-sensitive.</summary>
    public const string EmailMethod = "email";

    public EventRemindersValidator()
    {
        RuleFor(x => x.Overrides)
            .NotNull()
            .Must(o => o is null || o.Count <= MaxOverrides)
            .WithMessage($"An event can have at most {MaxOverrides} reminders.");

        // Mutually exclusive: Google answers a body carrying both with
        // 400 cannotUseDefaultRemindersAndSpecifyOverride. An empty Overrides alongside
        // UseDefault is the revert-to-default shape and is deliberately allowed.
        RuleFor(x => x)
            .Must(r => !r.UseDefault || r.Overrides.Count == 0)
            .WithMessage("Reminders either follow the calendar's default or replace it — not both.");

        RuleForEach(x => x.Overrides).ChildRules(reminder =>
        {
            reminder.RuleFor(r => r.Method)
                .Must(m => m is PopupMethod or EmailMethod)
                .WithMessage($"A reminder must be delivered by '{PopupMethod}' or '{EmailMethod}'.");

            reminder.RuleFor(r => r.Minutes)
                .InclusiveBetween(MinMinutes, MaxMinutes)
                .WithMessage($"A reminder must be between {MinMinutes} and {MaxMinutes} minutes before the event.");
        });
    }
}
