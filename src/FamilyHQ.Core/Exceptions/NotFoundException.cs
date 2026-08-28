namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A requested resource does not exist. Maps to HTTP 404.
/// </summary>
public abstract class NotFoundException : DomainException
{
    protected NotFoundException(string message, string? userMessage = null)
        : base(message, userMessage)
    {
    }

    protected NotFoundException(string message, Exception inner, string? userMessage = null)
        : base(message, inner, userMessage)
    {
    }
}
