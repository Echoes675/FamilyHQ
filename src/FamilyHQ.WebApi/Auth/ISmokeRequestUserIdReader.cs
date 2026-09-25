namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Reads the <c>userId</c> a smoke request asks for, from the raw request body (FHQ-139).
/// <para>
/// The smoke-account allowlist is an authorization requirement, and authorization runs before MVC
/// binds the body — so the id has to be read from the request itself. Behind an interface so the
/// requirement handler's decision can be unit-tested without a request at all.
/// </para>
/// </summary>
public interface ISmokeRequestUserIdReader
{
    /// <summary>
    /// Returns the requested user id, or <c>null</c> when the request does not name one — an absent,
    /// oversized, non-JSON or malformed body all read as "nobody". Never throws for a bad body, and
    /// always leaves the body readable for model binding.
    /// </summary>
    Task<string?> ReadUserIdAsync(HttpRequest request, CancellationToken ct = default);
}
