namespace FamilyHQ.WebApi.Models;

/// <summary>
/// Response body for a successful <c>POST /api/auth/issue-token</c> call.
/// </summary>
public sealed record IssueTokenResponse(string Token, DateTimeOffset ExpiresAt);
