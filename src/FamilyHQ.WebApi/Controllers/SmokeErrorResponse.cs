namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// The error body of the preprod smoke token endpoints (FHQ-139): a stable machine-readable
/// <see cref="Error"/> code plus a human sentence for whoever reads the pipeline log.
/// <para>
/// <see cref="ReauthRequiredErrorCode"/> is the important one. It is returned as <c>409 Conflict</c> and
/// is the one failure that is NOT a 404: a smoke run cannot fix a revoked Google grant by retrying, and
/// telling it apart from "misconfigured" is the difference between a useful preflight message — <i>sign
/// in to preprod again</i> — and an afternoon spent reading pipeline logs.
/// </para>
/// </summary>
public record SmokeErrorResponse(string Error, string Message)
{
    /// <summary>Google reports the stored grant revoked or expired. Returned with 409.</summary>
    public const string ReauthRequiredErrorCode = "reauth_required";

    /// <summary>The request body named no user. Returned with 400.</summary>
    public const string InvalidRequestErrorCode = "invalid_request";

    public static SmokeErrorResponse ReauthRequired() => new(
        ReauthRequiredErrorCode,
        "The stored Google grant for this account is no longer valid. Sign in to this environment "
        + "again to restore it.");

    public static SmokeErrorResponse InvalidRequest(string message) =>
        new(InvalidRequestErrorCode, message);
}
