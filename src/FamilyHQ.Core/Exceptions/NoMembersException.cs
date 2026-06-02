namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// An event was submitted with no members. At least one member is required. Maps to HTTP 400.
/// </summary>
public sealed class NoMembersException : DomainValidationException
{
    public NoMembersException()
        : base("At least one member is required.")
    {
    }
}
