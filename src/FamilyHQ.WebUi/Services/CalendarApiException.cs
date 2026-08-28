using System.Net;

namespace FamilyHQ.WebUi.Services;

/// <summary>
/// A non-success response from the FamilyHQ API on the calendar write path (FHQ-175). Replaces the
/// <see cref="HttpRequestException"/> that <c>EnsureSuccessStatusCode()</c> used to throw, which
/// discarded the response body — and with it the one piece of text that could have told the family
/// what to do next.
/// </summary>
/// <remarks>
/// <see cref="UserMessage"/> is the only member a component may render. It is present only when the
/// server opted that failure in (see <c>DomainExceptionHandler</c>); <see cref="Title"/> and
/// <see cref="Code"/> are for logs and for branching, never for display. The exception's own
/// <see cref="Exception.Message"/> carries the status and title only — never the raw body — so a
/// generic log of this exception leaks nothing the server did not already deem safe.
/// </remarks>
public sealed class CalendarApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? Title { get; }
    public string? UserMessage { get; }
    public string? Code { get; }

    public CalendarApiException(HttpStatusCode statusCode, ApiProblem problem)
        : base(BuildMessage(statusCode, problem.Title))
    {
        StatusCode = statusCode;
        Title = problem.Title;
        UserMessage = problem.UserMessage;
        Code = problem.Code;
    }

    private static string BuildMessage(HttpStatusCode statusCode, string? title) =>
        title is null
            ? $"Calendar API request failed with status {(int)statusCode} {statusCode}."
            : $"Calendar API request failed with status {(int)statusCode} {statusCode}: {title}.";
}
