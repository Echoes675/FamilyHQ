using Microsoft.AspNetCore.Authorization;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// FHQ-139 requirement three, and the one that actually prevents token theft: the request may only
/// name the configured smoke account. Even with the flag, the tier and the shared secret all wrong,
/// these endpoints can mint for that account and no other; exposing a family token would additionally
/// require someone to put a family Google <c>sub</c> into <c>Smoke:UserId</c>.
/// </summary>
public sealed class SmokeAccountRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Pure decision, so the rule can be read and tested without a request.
    /// <para>
    /// A request that names NO user satisfies this requirement deliberately. It can mint nothing — the
    /// action rejects it with 400 before it touches the token store — and deferring keeps the
    /// documented "malformed body → 400" contract, which a 404 here would replace with a lie. The cost
    /// is that a 400 confirms the shared secret was correct; that is accepted, because the secret is
    /// high-entropy, the endpoints are only reachable on a LAN-only environment, and this allowlist is
    /// the protection that does not depend on the secret staying secret.
    /// </para>
    /// </summary>
    public static bool IsSatisfied(string? requestedUserId, string? configuredSmokeUserId)
    {
        if (string.IsNullOrWhiteSpace(configuredSmokeUserId))
            return false;

        if (string.IsNullOrWhiteSpace(requestedUserId))
            return true;

        return string.Equals(requestedUserId, configuredSmokeUserId, StringComparison.Ordinal);
    }
}
