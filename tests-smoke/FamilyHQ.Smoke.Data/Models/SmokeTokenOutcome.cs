namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// What preprod's smoke token endpoints (FHQ-139) answered. Modelled as an outcome rather than an
/// exception because preflight's job is to <b>report</b> an unhealthy environment with an actionable
/// message, not to throw somewhere a caller has to guess about.
/// </summary>
public enum SmokeTokenOutcome
{
    /// <summary>A token was issued.</summary>
    Issued,

    /// <summary>409 <c>reauth_required</c> — the stored Google grant is revoked. Someone must sign in again.</summary>
    ReauthRequired,

    /// <summary>
    /// 404 — indistinguishable from an unknown route by design. The endpoint is disabled, the tier is
    /// production, the shared secret is wrong, or the account has no stored Google connection.
    /// </summary>
    NotAvailable,

    /// <summary>400 — the request named no user. A caller bug, and proof the shared secret was accepted.</summary>
    BadRequest,

    /// <summary>Anything else. The status code travels alongside so a failure message can name it.</summary>
    Unexpected
}
