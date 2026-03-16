using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// Scoped holder for a pre-obtained OAuth access token.
/// When set, GoogleCalendarClient uses this directly instead of
/// exchanging a refresh token for a new access token.
/// </summary>
public class AccessTokenProvider : IAccessTokenProvider
{
    public string? AccessToken { get; set; }
}
