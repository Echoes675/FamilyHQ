using System.Net;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// Thrown for non-auth 4xx/5xx responses from the Google Calendar API.
/// Distinct from <see cref="GoogleReauthRequiredException"/> so callers can decide
/// between "user must reconnect" (409) and "upstream error" (502) handling.
/// Deliberately does NOT retain the raw Google response body (FHQ-88) — the status code and
/// operation are enough for handlers; diagnostic detail belongs in the parsed, structured logs.
/// </summary>
public class GoogleApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Operation { get; }

    /// <summary>
    /// The upstream <c>Retry-After</c> delay when Google supplied one (typically on a 429),
    /// normalised to a positive <see cref="TimeSpan"/>. Null when absent, unparseable, or non-positive.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public GoogleApiException(HttpStatusCode statusCode, string operation, TimeSpan? retryAfter = null)
        : base($"Google API {operation} failed with status {(int)statusCode} {statusCode}.")
    {
        StatusCode = statusCode;
        Operation = operation;
        RetryAfter = retryAfter;
    }
}
