namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// The smoke-account allowlist (FHQ-139). This is the requirement that actually prevents token
/// theft: even with the flag, the tier and the shared secret all wrong, the smoke endpoints can only
/// ever mint credentials for this one account — never for a family member. Exposing a family token
/// would additionally require someone to put a family Google <c>sub</c> into this setting.
/// <para>
/// Unset is a deny, never a wildcard.
/// </para>
/// </summary>
public class SmokeOptions
{
    public const string SectionName = "Smoke";

    /// <summary>
    /// The Google subject identifier (<c>sub</c>) of the smoke test account, as stored in
    /// <c>UserToken.UserId</c>. Not a secret — an account identifier — but still never logged
    /// alongside anything that identifies the family.
    /// </summary>
    public string? UserId { get; set; }
}
