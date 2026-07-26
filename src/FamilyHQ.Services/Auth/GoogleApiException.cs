using System.Net;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// Thrown for non-auth 4xx/5xx responses from the Google Calendar API.
/// Distinct from <see cref="GoogleReauthRequiredException"/> so callers can decide
/// between "user must reconnect" (409) and "upstream error" (502) handling.
/// </summary>
public class GoogleApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string Operation { get; }
    public string? ResponseBody { get; }

    /// <summary>
    /// The upstream <c>Retry-After</c> delay when Google supplied one (typically on a 429),
    /// normalised to a positive <see cref="TimeSpan"/>. Null when absent, unparseable, or non-positive.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public GoogleApiException(HttpStatusCode statusCode, string operation, string? responseBody, TimeSpan? retryAfter = null)
        : base($"Google API {operation} failed with status {(int)statusCode} {statusCode}.")
    {
        StatusCode = statusCode;
        Operation = operation;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }
}
