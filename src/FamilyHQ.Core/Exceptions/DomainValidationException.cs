namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A client-fixable input error or business-rule violation. Maps to HTTP 400.
/// </summary>
public abstract class DomainValidationException : DomainException
{
    protected DomainValidationException(string message, string? userMessage = null)
        : base(message, userMessage)
    {
    }

    protected DomainValidationException(string message, Exception inner, string? userMessage = null)
        : base(message, inner, userMessage)
    {
    }
}
