namespace FamilyHQ.WebApi.Models;

/// <summary>
/// Response body for a successful <c>POST /api/auth/issue-token</c> call.
/// </summary>
public class IssueTokenResponse
{
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
