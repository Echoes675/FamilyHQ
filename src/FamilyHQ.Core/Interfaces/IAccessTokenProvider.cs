namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Scoped service that holds a pre-obtained OAuth access token.
/// Set during login to allow the initial sync to use the fresh token
/// instead of going through the refresh token flow.
/// </summary>
public interface IAccessTokenProvider
{
    string? AccessToken { get; set; }
}
