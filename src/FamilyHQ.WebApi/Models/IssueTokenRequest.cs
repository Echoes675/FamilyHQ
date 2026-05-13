namespace FamilyHQ.WebApi.Models;

/// <summary>
/// Body of <c>POST /api/auth/issue-token</c>. Identifies the user the trusted caller wants a
/// JWT for. <see cref="UserId"/> is the Google subject identifier stored as
/// <c>UserToken.UserId</c>.
/// </summary>
public sealed record IssueTokenRequest(string UserId);
