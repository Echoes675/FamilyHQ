namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A member change was requested at a recurring scope other than "All events". Member changes apply
/// to the whole series and are only permitted at the AllInSeries scope. Maps to HTTP 400.
/// </summary>
/// <remarks>
/// <see cref="UserFacingMessage"/> is the single source of the sentence the kiosk shows for this
/// rule — <c>RecurrenceScopePrompt</c> renders it pre-emptively when a member change is pending at
/// the wrong scope, and the server returns it when the rule is hit anyway (FHQ-175).
/// </remarks>
public sealed class MemberScopeViolationException : DomainValidationException
{
    public const string UserFacingMessage =
        "Member changes apply to the whole series. Select \"All events\" to continue.";

    public MemberScopeViolationException()
        : base(
            "Member changes apply to the whole series and are only permitted at the 'All events' scope.",
            UserFacingMessage)
    {
    }
}
