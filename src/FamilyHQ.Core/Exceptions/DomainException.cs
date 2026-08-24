namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// Base type for all domain-level failures FamilyHQ raises deliberately. The presentation layer
/// maps these to HTTP status codes in one place (see DomainExceptionHandler) so the HTTP contract
/// never depends on exception message text. Untyped framework exceptions (e.g. a raw
/// <see cref="InvalidOperationException"/> signalling a server precondition) are deliberately NOT
/// derived from this type so they surface as 500.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two audiences, two strings (FHQ-175).</b> <see cref="Exception.Message"/> is for logs and
/// direct API clients: it reaches Seq and <c>ProblemDetails.Detail</c>, and it may name internal
/// ids, property names and RFC-5545 terms. <see cref="UserMessage"/> is the text the kiosk puts in
/// front of the family, and it is <b>opt-in</b>: an exception that does not set it gets the kiosk's
/// generic fallback ("Couldn't save your event — please try again."). Only set it when the text has
/// been written for a 7" touchscreen and carries its own advice — the kiosk suppresses the retry hint
/// whenever a user message is present, on the grounds that a vetted message says what to do next.
/// </para>
/// <para>
/// The decision is made here rather than in the client on purpose: a client choosing which server
/// strings are safe to show would be guessing about text it does not own, and would start leaking
/// the moment a server message changed. <c>DomainExceptionHandlerTests</c> enumerates every concrete
/// subtype so a new exception cannot opt in by accident.
/// </para>
/// </remarks>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Text fit to show a family member on the kiosk, or null when only the generic fallback should
    /// be shown. Must not carry identifiers, Google calendar ids (they are email addresses), place
    /// names or any other internal detail — it is rendered verbatim.
    /// </summary>
    public string? UserMessage { get; }

    protected DomainException(string message, string? userMessage = null) : base(message)
    {
        UserMessage = userMessage;
    }

    protected DomainException(string message, Exception inner, string? userMessage = null) : base(message, inner)
    {
        UserMessage = userMessage;
    }
}
