using FamilyHQ.WebApi.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Evaluates <see cref="SmokeAccountRequirement"/> against the user id the request asks for, and
/// audits the decision. The requested id is never echoed into the log: a denial means the value was
/// NOT the smoke account, so it may be a family member's Google subject identifier, and pairing that
/// with a rejected smoke request is more than the log needs.
/// </summary>
public sealed class SmokeAccountRequirementHandler(
    IHttpContextAccessor httpContextAccessor,
    ISmokeRequestUserIdReader userIdReader,
    IOptions<SmokeOptions> smokeOptions,
    ILogger<SmokeAccountRequirementHandler> logger)
    : AuthorizationHandler<SmokeAccountRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SmokeAccountRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            logger.LogWarning(
                "Preprod smoke access denied — the smoke-account requirement was evaluated without a request.");
            return;
        }

        var configuredSmokeUserId = smokeOptions.Value.UserId;
        var requestedUserId = await userIdReader.ReadUserIdAsync(httpContext.Request, httpContext.RequestAborted);

        if (SmokeAccountRequirement.IsSatisfied(requestedUserId, configuredSmokeUserId))
        {
            context.Succeed(requirement);
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredSmokeUserId))
        {
            logger.LogWarning(
                "Preprod smoke access denied — {SmokeSection}:{SmokeUserIdKey} is not configured, so no "
                + "account is allowlisted.",
                SmokeOptions.SectionName, nameof(SmokeOptions.UserId));
            return;
        }

        logger.LogWarning(
            "Preprod smoke access denied — the request asked for an account that is not the configured "
            + "smoke account.");
    }
}
