namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// The result of <c>POST /api/auth/issue-google-access-token</c>. On success it is the suite's oracle
/// credential: it talks to Google directly, so an assertion about what Google holds never has to trust
/// FamilyHQ's own read path.
/// </summary>
public sealed record SmokeGoogleAccessToken(
    SmokeTokenOutcome Outcome, string? AccessToken, DateTimeOffset? ExpiresAt, int StatusCode);
