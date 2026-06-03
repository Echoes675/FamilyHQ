namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A requested resource does not exist. Maps to HTTP 404.
/// </summary>
public abstract class NotFoundException : DomainException
{
    protected NotFoundException(string message) : base(message)
    {
    }

    protected NotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}
