using System.Text.Json;

namespace FamilyHQ.WebUi.Services;

/// <summary>
/// The parts of an RFC 7807 ProblemDetails body the kiosk acts on (FHQ-175). Every member is
/// optional because not every failure body is a ProblemDetails: <c>EventsController</c> returns raw
/// FluentValidation arrays and bare JSON strings that bypass <c>DomainExceptionHandler</c>, a proxy
/// can answer with HTML, and a dead socket answers with nothing. <see cref="Parse"/> therefore
/// never throws and never surfaces anything it did not positively recognise.
/// </summary>
/// <param name="Title">The ProblemDetails <c>title</c>, when present.</param>
/// <param name="UserMessage">
/// The <c>userMessage</c> extension — the only server text the kiosk renders verbatim. Null means
/// "show the generic fallback"; the server opts specific failures in, the client never guesses.
/// </param>
/// <param name="Code">The <c>code</c> extension (e.g. <c>needs_reauth</c>), when present.</param>
public sealed record ApiProblem(string? Title, string? UserMessage, string? Code)
{
    public static readonly ApiProblem Empty = new(null, null, null);

    private const string TitleProperty = "title";
    private const string UserMessageProperty = "userMessage";
    private const string CodeProperty = "code";

    /// <summary>
    /// Reads the recognised members out of a response body. A body that is empty, not JSON, or JSON
    /// whose root is not an object (an array, a bare string) yields <see cref="Empty"/> — the shape
    /// mismatch must fall back cleanly, never surface raw internals.
    /// </summary>
    public static ApiProblem Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;

            return new ApiProblem(
                ReadString(root, TitleProperty),
                ReadString(root, UserMessageProperty),
                ReadString(root, CodeProperty));
        }
        catch (JsonException)
        {
            // Malformed JSON (an HTML error page, a truncated body). Nothing recognisable to show.
            return Empty;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
