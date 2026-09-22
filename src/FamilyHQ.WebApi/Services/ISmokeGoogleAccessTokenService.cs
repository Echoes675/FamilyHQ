namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Turns the smoke account's STORED Google refresh token into a fresh Google access token (FHQ-139),
/// so the smoke suite can check Google directly rather than trusting FamilyHQ's own read path.
/// <para>
/// Chosen over holding a separate refresh token in Jenkins: whatever grant preprod holds is the one the
/// tests use, so signing preprod in again is the only upkeep.
/// </para>
/// </summary>
public interface ISmokeGoogleAccessTokenService
{
    Task<SmokeGoogleAccessTokenResult> IssueAsync(string userId, CancellationToken ct = default);
}
