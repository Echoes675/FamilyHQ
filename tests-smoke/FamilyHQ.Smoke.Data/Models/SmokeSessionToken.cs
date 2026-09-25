namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// The result of <c>POST /api/auth/issue-token</c>. Carries the JWT only on
/// <see cref="SmokeTokenOutcome.Issued"/>; the expiry lives inside the token's own <c>exp</c> claim,
/// which is why the endpoint publishes no second copy of it.
/// </summary>
public sealed record SmokeSessionToken(SmokeTokenOutcome Outcome, string? Token, int StatusCode);
