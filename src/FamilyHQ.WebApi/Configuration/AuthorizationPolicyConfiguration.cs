using Microsoft.AspNetCore.Authorization;

namespace FamilyHQ.WebApi.Configuration;

/// <summary>
/// Registers the deny-by-default authorization posture (FHQ-98): any endpoint without explicit
/// authorization metadata falls back to requiring an authenticated user, so a controller that
/// forgets [Authorize] is locked down instead of silently public. Genuinely public endpoints
/// must opt out with an explicit [AllowAnonymous].
/// </summary>
public static class AuthorizationPolicyConfiguration
{
    public static IServiceCollection AddFallbackAuthorizationPolicy(this IServiceCollection services) =>
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
}
