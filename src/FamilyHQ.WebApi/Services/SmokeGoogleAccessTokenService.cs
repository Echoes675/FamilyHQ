using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;

namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Reads the stored refresh token through the existing token store and refreshes it through the existing
/// refresh path (<see cref="IGoogleTokenRefresher"/>, implemented by <c>GoogleAuthService</c>) — there is
/// deliberately no second refresh implementation here.
/// </summary>
public sealed class SmokeGoogleAccessTokenService(
    ITokenStore tokenStore,
    IGoogleTokenRefresher tokenRefresher,
    TimeProvider timeProvider,
    ILogger<SmokeGoogleAccessTokenService> logger) : ISmokeGoogleAccessTokenService
{
    public async Task<SmokeGoogleAccessTokenResult> IssueAsync(
        string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var refreshToken = await tokenStore.GetRefreshTokenAsync(userId, ct);
        if (string.IsNullOrEmpty(refreshToken))
        {
            logger.LogInformation(
                "No Google refresh token is stored for user {UserId}; no access token can be issued.",
                userId);
            return SmokeGoogleAccessTokenResult.UnknownUser();
        }

        // FHQ-86/FHQ-139: Google may rotate the refresh token in a refresh-grant response, and
        // GoogleAuthService persists the replacement through the CURRENT-USER token store overload. This
        // request authenticates a machine (the SmokeClient scheme deliberately carries no sub claim), so
        // nothing would resolve ICurrentUserService: the save would throw, the stored grant would go
        // stale, and the next smoke run would report "sign in again" for no reason. Make the user ambient
        // for the duration of the refresh, exactly as the login callback does for the initial sync.
        BackgroundUserContext.Current = userId;
        try
        {
            var (accessToken, expiresIn) = await tokenRefresher.RefreshAccessTokenAsync(refreshToken, ct);
            var expiresAt = timeProvider.GetUtcNow().AddSeconds(expiresIn);

            // The token value is never logged — only when it expires.
            logger.LogInformation(
                "Issued a Google access token for user {UserId}; it expires at {AccessTokenExpiresAt}.",
                userId, expiresAt);
            return SmokeGoogleAccessTokenResult.Issued(accessToken, expiresAt);
        }
        catch (GoogleReauthRequiredException ex)
        {
            // Narrow and deliberate: a revoked grant is the one failure this endpoint must REPORT rather
            // than propagate, because the preflight turns it into "sign in to preprod again". Information,
            // not Error — an expected, handled condition (FHQ-56). The parsed error description is not
            // logged here; GoogleAuthService has already logged Google's parsed error codes.
            logger.LogInformation(
                "The stored Google grant for user {UserId} is no longer valid ({GoogleFailureSource}); "
                + "the environment must be signed in again.",
                userId, ex.FailureSource);
            return SmokeGoogleAccessTokenResult.ReauthRequired();
        }
        finally
        {
            BackgroundUserContext.Current = null;
        }
    }
}
