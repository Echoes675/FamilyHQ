namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// Response body of <c>POST /api/auth/issue-token</c> (FHQ-139). Deliberately carries the token only:
/// the expiry is already inside the JWT's <c>exp</c> claim, and publishing a second copy would mean
/// duplicating the lifetime policy that <c>IJwtTokenService</c> owns.
/// </summary>
public record IssueTokenResponse(string Token);
