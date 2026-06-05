namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// An unrecognised <c>RecurrenceScope</c> value was supplied. Client-fixable input error;
/// maps to HTTP 400.
/// </summary>
public sealed class UnknownRecurrenceScopeException : DomainValidationException
{
    public UnknownRecurrenceScopeException(object scope)
        : base($"Unknown recurrence scope '{scope}'.")
    {
    }
}
