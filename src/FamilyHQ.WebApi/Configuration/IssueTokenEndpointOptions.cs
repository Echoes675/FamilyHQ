namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Configuration for the trusted-system <c>POST /api/auth/issue-token</c> endpoint.
/// The endpoint is opt-in (<see cref="Enabled"/> defaults to <c>false</c>) and exists only to
/// let non-prod smoke pipelines mint a JWT for a pre-existing user without driving the Google
/// OAuth UI. It must never be enabled in production.
/// </summary>
public class IssueTokenEndpointOptions
{
    public const string SectionName = "Auth:IssueTokenEndpoint";

    /// <summary>
    /// When <c>false</c> (the default) the endpoint returns 404 regardless of the request.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Shared secret presented by trusted callers via the <c>Authorization: Bearer &lt;secret&gt;</c>
    /// header. Compared with constant-time semantics; never written to logs.
    /// </summary>
    public string Secret { get; set; } = string.Empty;
}
