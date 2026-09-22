namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// Response body of <c>POST /api/auth/issue-google-access-token</c> (FHQ-139). The expiry is Google's
/// own <c>expires_in</c>, resolved against the server clock, so the smoke suite can reuse one token for
/// the run rather than refreshing per assertion.
/// </summary>
public record IssueGoogleAccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
