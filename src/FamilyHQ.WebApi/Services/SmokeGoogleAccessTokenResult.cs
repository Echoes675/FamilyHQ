namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Outcome of a smoke Google access-token request (FHQ-139). The token travels on this result and
/// nowhere else — never into a log, an exception message or a structured property.
/// </summary>
public sealed record SmokeGoogleAccessTokenResult
{
    private SmokeGoogleAccessTokenResult(
        SmokeGoogleAccessTokenOutcome outcome, string? accessToken, DateTimeOffset? expiresAt)
    {
        Outcome = outcome;
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
    }

    public SmokeGoogleAccessTokenOutcome Outcome { get; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="SmokeGoogleAccessTokenOutcome.Issued"/>.</summary>
    public string? AccessToken { get; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="SmokeGoogleAccessTokenOutcome.Issued"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    public static SmokeGoogleAccessTokenResult Issued(string accessToken, DateTimeOffset expiresAt) =>
        new(SmokeGoogleAccessTokenOutcome.Issued, accessToken, expiresAt);

    public static SmokeGoogleAccessTokenResult UnknownUser() =>
        new(SmokeGoogleAccessTokenOutcome.UnknownUser, accessToken: null, expiresAt: null);

    public static SmokeGoogleAccessTokenResult ReauthRequired() =>
        new(SmokeGoogleAccessTokenOutcome.ReauthRequired, accessToken: null, expiresAt: null);
}
