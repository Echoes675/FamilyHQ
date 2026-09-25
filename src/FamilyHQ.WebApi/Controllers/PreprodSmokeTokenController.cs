using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

/// <summary>
/// The two deliberately powerful test-only endpoints the preprod smoke suite needs (FHQ-139):
/// <list type="bullet">
///   <item><description>
///     <c>POST /api/auth/issue-token</c> mints a FamilyHQ JWT for a user whose Google refresh token is
///     already stored, so the headless browser can start signed in. It never talks to Google.
///   </description></item>
///   <item><description>
///     <c>POST /api/auth/issue-google-access-token</c> refreshes that stored grant and hands back a live
///     Google access token, so the suite can check Google directly rather than trusting FamilyHQ's read
///     path.
///   </description></item>
/// </list>
/// <para>
/// <b>Both are gated by the <c>PreprodSmokeAccess</c> policy at CLASS level</b> — not by checks inside
/// the actions — so a new action here cannot be added unguarded, and all three requirements (a
/// non-production tier with the flag on, the shared secret via the <c>SmokeClient</c> scheme, and a
/// request naming only the configured smoke account) are evaluated before any action code runs. Every
/// policy failure surfaces as 404, from the scheme's challenge/forbid handlers.
/// </para>
/// <para>
/// <b>Response codes.</b> 404 for anything not allowed and for an unknown user — structurally identical
/// to an unknown route, so there is no recon signal. 400 for a body that names nobody: it can mint
/// nothing, and a 404 there would hide a broken caller. 409 with <c>reauth_required</c> for a revoked
/// Google grant, which is the one failure a smoke preflight must act on rather than retry.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize(Policy = PreprodSmokeAccessPolicy.Name)]
public sealed class PreprodSmokeTokenController(
    ITokenStore tokenStore,
    IJwtTokenService jwtTokenService,
    ISmokeGoogleAccessTokenService googleAccessTokenService,
    ILogger<PreprodSmokeTokenController> logger) : ControllerBase
{
    private const string UserIdRequiredMessage = "userId is required.";

    [HttpPost("issue-token")]
    public async Task<IActionResult> IssueToken([FromBody] SmokeTokenRequest? request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.UserId))
            return BadRequest(SmokeErrorResponse.InvalidRequest(UserIdRequiredMessage));

        // Existence is checked through the token store's own user list rather than by reading the token:
        // the answer needed here is "has this account connected Google?", and pulling a refresh token
        // into the controller to find that out would put a secret in scope for no reason.
        var storedUserIds = await tokenStore.GetAllUserIdsAsync(ct);
        if (!storedUserIds.Contains(request.UserId, StringComparer.Ordinal))
        {
            logger.LogWarning(
                "Smoke JWT issue refused for user {UserId} — no stored Google connection for that account.",
                request.UserId);
            return NotFound();
        }

        // No email claim: UserToken does not persist one, and everything downstream keys off sub. The
        // JWT's claims and lifetime stay owned by IJwtTokenService (FHQ-126) — this endpoint changes who
        // may ask for a token, never what a token is.
        var token = jwtTokenService.GenerateToken(request.UserId, email: null);

        // OAuth BCP, as on renew-jwt: a token response must never be cached.
        Response.Headers.CacheControl = "no-store";

        // Audit line. The token itself is never logged, here or anywhere.
        logger.LogInformation("Smoke JWT issued for user {UserId}.", request.UserId);
        return Ok(new IssueTokenResponse(token));
    }

    [HttpPost("issue-google-access-token")]
    public async Task<IActionResult> IssueGoogleAccessToken(
        [FromBody] SmokeTokenRequest? request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.UserId))
            return BadRequest(SmokeErrorResponse.InvalidRequest(UserIdRequiredMessage));

        var result = await googleAccessTokenService.IssueAsync(request.UserId, ct);

        Response.Headers.CacheControl = "no-store";

        switch (result.Outcome)
        {
            case SmokeGoogleAccessTokenOutcome.Issued:
                // Audit line. The access token travels in the response body only.
                logger.LogInformation(
                    "Smoke Google access token issued for user {UserId}.", request.UserId);
                return Ok(new IssueGoogleAccessTokenResponse(result.AccessToken!, result.ExpiresAt!.Value));

            case SmokeGoogleAccessTokenOutcome.ReauthRequired:
                logger.LogWarning(
                    "Smoke Google access token refused for user {UserId} — the stored grant is no longer "
                    + "valid; this environment must be signed in again.",
                    request.UserId);
                return Conflict(SmokeErrorResponse.ReauthRequired());

            default:
                logger.LogWarning(
                    "Smoke Google access token refused for user {UserId} — no stored Google connection "
                    + "for that account.",
                    request.UserId);
                return NotFound();
        }
    }
}
