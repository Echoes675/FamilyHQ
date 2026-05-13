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

    public GoogleApiException(HttpStatusCode statusCode, string operation, string? responseBody)
        : base($"Google API {operation} failed with status {(int)statusCode} {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        Operation = operation;
        ResponseBody = responseBody;
    }
}
