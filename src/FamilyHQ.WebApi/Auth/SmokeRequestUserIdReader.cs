using System.Text.Json;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Default <see cref="ISmokeRequestUserIdReader"/>: buffers the request body, reads the
/// <c>userId</c> property out of it, and rewinds so MVC can bind the same body afterwards.
/// </summary>
public sealed class SmokeRequestUserIdReader : ISmokeRequestUserIdReader
{
    /// <summary>
    /// The body is <c>{ "userId": "&lt;google sub&gt;" }</c> — a few dozen bytes. Anything beyond this
    /// is refused outright rather than buffered, which also keeps the buffered body inside ASP.NET
    /// Core's in-memory threshold so nothing ever spools to disk.
    /// </summary>
    internal const int MaxBodyBytes = 4 * 1024;

    private const string UserIdPropertyName = "userId";

    public async Task<string?> ReadUserIdAsync(HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasJsonContentType())
            return null;

        // A request with no declared length (chunked) is not read here. The other two requirements
        // still apply, and the action answers the body itself.
        if (request.ContentLength is null or <= 0 or > MaxBodyBytes)
            return null;

        var length = (int)request.ContentLength.Value;
        request.EnableBuffering();

        var buffer = new byte[length];
        try
        {
            var read = await request.Body.ReadAtLeastAsync(
                buffer, length, throwOnEndOfStream: false, ct);
            return ExtractUserId(buffer.AsSpan(0, read));
        }
        finally
        {
            // Unconditional rewind: model binding must see the whole body whatever happened above.
            if (request.Body.CanSeek)
                request.Body.Position = 0;
        }
    }

    private static string? ExtractUserId(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            // Case-insensitive, because MVC binds property names that way: a body MVC would accept
            // must resolve to the same user id here, or the allowlist and the action would disagree.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals(UserIdPropertyName)
                    && !string.Equals(property.Name, UserIdPropertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
            }

            return null;
        }
        catch (JsonException)
        {
            // A body that is not JSON names nobody. Deliberately narrow and deliberately silent: the
            // requirement handler audits the resulting decision, and the action returns 400 for the
            // same body, so nothing is swallowed.
            return null;
        }
    }
}
