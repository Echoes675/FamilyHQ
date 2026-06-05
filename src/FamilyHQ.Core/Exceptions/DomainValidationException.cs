namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A client-fixable input error or business-rule violation. Maps to HTTP 400.
/// </summary>
public abstract class DomainValidationException : DomainException
{
    protected DomainValidationException(string message) : base(message)
    {
    }

    protected DomainValidationException(string message, Exception inner) : base(message, inner)
    {
    }
}
