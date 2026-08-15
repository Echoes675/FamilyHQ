namespace FamilyHQ.Services.Auth;

/// <summary>
/// Thrown when Google rejects an OAuth token (invalid_grant on refresh, or 401/403 on Calendar API).
/// Indicates the user must re-consent to restore connectivity.
/// Deliberately carries only the PARSED OAuth <c>error_description</c>/reason-phrase — never the raw
/// Google response body — so no handler or logger up the stack can leak Google error text (FHQ-88).
/// </summary>
public class GoogleReauthRequiredException : Exception
{
    public string? UserId { get; }
    public string? ErrorDescription { get; }
    public GoogleAuthFailureSource FailureSource { get; }

    public GoogleReauthRequiredException(
        GoogleAuthFailureSource source,
        string? errorDescription,
        string? userId = null)
        : base($"Google re-authentication required ({source}): {errorDescription ?? "no description"}")
    {
        FailureSource = source;
        ErrorDescription = errorDescription;
        UserId = userId;
    }
}
