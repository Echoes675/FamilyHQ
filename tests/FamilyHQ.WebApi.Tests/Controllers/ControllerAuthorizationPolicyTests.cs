using System.Reflection;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

/// <summary>
/// Architecture tests for the deny-by-default authorization posture (FHQ-98). The fallback
/// policy makes unannotated endpoints require authentication, so the only way an endpoint
/// can be public is an explicit [AllowAnonymous] — and that inventory is pinned here.
/// Adding a public endpoint without updating the approved list below fails the build.
/// </summary>
public class ControllerAuthorizationPolicyTests
{
    /// <summary>
    /// The ONLY controller actions approved to be reachable without authentication.
    /// - AuthController.Login: starts the OAuth flow; no session can exist yet.
    /// - AuthController.Callback: Google redirects the browser here without a JWT.
    /// - AuthController.Exchange: one-time-code-for-JWT exchange happens pre-auth.
    /// - HealthController.Get: deploy checks and E2E probes hit /api/health unauthenticated.
    /// - DayThemeController.GetToday: kiosk renders the day theme before login.
    /// - SyncController.GooglePushWebhook: Google push sends no bearer token; authenticity
    ///   is enforced via the per-channel token instead (FHQ-81).
    /// </summary>
    private static readonly IReadOnlySet<string> ApprovedAnonymousActions = new HashSet<string>
    {
        "AuthController.Login",
        "AuthController.Callback",
        "AuthController.Exchange",
        "HealthController.Get",
        "DayThemeController.GetToday",
        "SyncController.GooglePushWebhook",
    };

    [Fact]
    public void ControllerActions_WhenAudited_EveryActionDeclaresAuthorizeOrAllowAnonymous()
    {
        // Arrange
        var actions = GetControllerActions();

        // Act
        var undeclared = actions
            .Where(action => !HasAuthorizeMetadata(action) && !IsEffectivelyAnonymous(action))
            .Select(ActionName)
            .OrderBy(name => name)
            .ToList();

        // Assert
        undeclared.Should().BeEmpty(
            "every controller action must state its posture explicitly — [Authorize] (action or " +
            "controller level) or [AllowAnonymous]; the FHQ-98 fallback policy locks down " +
            "unannotated endpoints, but the posture must still be readable at the controller");
    }

    [Fact]
    public void ControllerActions_WhenAudited_AnonymousActionsMatchApprovedPublicInventory()
    {
        // Arrange
        var actions = GetControllerActions();

        // Act
        var anonymousActions = actions
            .Where(IsEffectivelyAnonymous)
            .Select(ActionName)
            .ToList();

        // Assert
        anonymousActions.Should().BeEquivalentTo(ApprovedAnonymousActions,
            "the set of publicly-reachable endpoints is pinned by FHQ-98 — a new [AllowAnonymous] " +
            "must be a deliberate decision recorded in this approved inventory");
    }

    private static string ActionName(MethodInfo action) =>
        $"{action.DeclaringType!.Name}.{action.Name}";

    private static List<MethodInfo> GetControllerActions() =>
        typeof(HealthController).Assembly
            .GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && type.IsPublic && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute), inherit: true))
            .ToList();

    private static bool HasAuthorizeMetadata(MethodInfo action) =>
        action.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any() ||
        action.DeclaringType!.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any();

    private static bool IsEffectivelyAnonymous(MethodInfo action) =>
        action.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any() ||
        action.DeclaringType!.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
}
