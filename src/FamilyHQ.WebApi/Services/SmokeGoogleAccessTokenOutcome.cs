namespace FamilyHQ.WebApi.Services;

/// <summary>
/// The three answers <see cref="ISmokeGoogleAccessTokenService"/> can give (FHQ-139). They are distinct
/// because the smoke preflight must be able to tell "this environment was never signed in" and "the
/// grant it held has been revoked — sign in to preprod again" apart from each other.
/// </summary>
public enum SmokeGoogleAccessTokenOutcome
{
    /// <summary>A fresh Google access token was obtained.</summary>
    Issued,

    /// <summary>No Google refresh token is stored for that user.</summary>
    UnknownUser,

    /// <summary>Google rejected the stored grant; the environment must be signed in again.</summary>
    ReauthRequired
}
