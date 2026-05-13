namespace FamilyHQ.Services.Auth;

/// <summary>
/// Thrown when Google rejects an OAuth token (invalid_grant on refresh, or 401/403 on Calendar API).
/// Indicates the user must re-consent to restore connectivity.
/// </summary>
public class GoogleReauthRequiredException : Exception
{
    public string? UserId { get; }
    public string? ErrorDescription { get; }
    public new GoogleAuthFailureSource Source { get; }
    public string? ResponseBody { get; }

    public GoogleReauthRequiredException(
        GoogleAuthFailureSource source,
        string? errorDescription,
        string? responseBody = null,
        string? userId = null)
        : base($"Google re-authentication required ({source}): {errorDescription ?? "no description"}")
    {
        Source = source;
        ErrorDescription = errorDescription;
        ResponseBody = responseBody;
        UserId = userId;
    }
}
