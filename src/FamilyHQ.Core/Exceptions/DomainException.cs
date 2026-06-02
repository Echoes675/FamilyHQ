namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// Base type for all domain-level failures FamilyHQ raises deliberately. The presentation layer
/// maps these to HTTP status codes in one place (see DomainExceptionHandler) so the HTTP contract
/// never depends on exception message text. Untyped framework exceptions (e.g. a raw
/// <see cref="InvalidOperationException"/> signalling a server precondition) are deliberately NOT
/// derived from this type so they surface as 500.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception inner) : base(message, inner)
    {
    }
}
