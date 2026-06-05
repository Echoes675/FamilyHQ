namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A member change was requested at a recurring scope other than "All events". Member changes apply
/// to the whole series and are only permitted at the AllInSeries scope. Maps to HTTP 400.
/// </summary>
public sealed class MemberScopeViolationException : DomainValidationException
{
    public MemberScopeViolationException()
        : base("Member changes apply to the whole series and are only permitted at the 'All events' scope.")
    {
    }
}
