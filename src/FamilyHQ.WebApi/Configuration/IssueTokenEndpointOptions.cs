namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Configuration for the preprod smoke token endpoints (FHQ-139): <c>POST /api/auth/issue-token</c>
/// and <c>POST /api/auth/issue-google-access-token</c>. Both exist so an unattended smoke pipeline
/// can obtain credentials for the smoke account without driving Google's consent UI, which is
/// bot-detection prone from CI.
/// <para>
/// Opt-in: <see cref="Enabled"/> defaults to <c>false</c>, and being enabled is only one of the
/// conditions the <c>PreprodSmokeAccess</c> policy requires — the tier must also be non-production
/// and the request must name the configured smoke account.
/// </para>
/// </summary>
public class IssueTokenEndpointOptions
{
    public const string SectionName = "Auth:IssueTokenEndpoint";

    /// <summary>
    /// When <c>false</c> (the default) the endpoints are unreachable — every request 404s.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Shared secret presented as <c>Authorization: Bearer &lt;secret&gt;</c> and validated by the
    /// <c>SmokeClient</c> authentication scheme with a constant-time comparison. Never logged, never
    /// committed, and never placed in an exception message.
    /// </summary>
    public string? Secret { get; set; }
}
