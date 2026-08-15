using System.Reflection;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

/// <summary>
/// Architecture tests for the deny-by-default authorization posture (FHQ-98). The fallback
/// policy makes unannotated endpoints require authentication, so the only way a controller
/// action can be public is an explicit [AllowAnonymous] — and that inventory is pinned here.
///
/// Scope: this scan reflects over MVC-routable controller classes in the FamilyHQ.WebApi
/// assembly only — ControllerBase descendants plus [Controller]/"*Controller"-named POCOs,
/// including actions inherited from base classes. It does NOT cover non-controller endpoints:
/// the SignalR hub (/hubs/calendar, including its negotiate), MapOpenApi, and the Scalar UI
/// are mapped with .AllowAnonymous() in Program.cs and are exercised only at runtime and by
/// the E2E suite. Anything this scan cannot see is still locked down at runtime by the
/// fallback policy — a gap here is inventory visibility, not reachability.
/// </summary>
public class ControllerAuthorizationPolicyTests
{
    /// <summary>
    /// The ONLY controller actions approved to be reachable without authentication, pinned by
    /// full type name, HTTP verb(s), and resolved route template so a verb or route change on
    /// an approved action fails this test.
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
        "FamilyHQ.WebApi.Controllers.AuthController.Login [GET api/auth/login]",
        "FamilyHQ.WebApi.Controllers.AuthController.Callback [GET api/auth/callback]",
        "FamilyHQ.WebApi.Controllers.AuthController.Exchange [POST api/auth/exchange]",
        "FamilyHQ.WebApi.Controllers.HealthController.Get [GET api/health]",
        "FamilyHQ.WebApi.Controllers.DayThemeController.GetToday [GET api/daytheme/today]",
        "FamilyHQ.WebApi.Controllers.SyncController.GooglePushWebhook [POST api/sync/webhook]",
    };

    [Fact]
    public void ControllerActions_WhenAudited_EveryActionDeclaresAuthorizeOrAllowAnonymous()
    {
        // Arrange
        var actions = GetControllerActions();

        // Act
        var undeclared = actions
            .Where(action => !HasAuthorizeMetadata(action) && !IsEffectivelyAnonymous(action))
            .Select(ActionIdentity)
            .OrderBy(identity => identity)
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
            .Select(ActionIdentity)
            .ToList();

        // Assert
        anonymousActions.Should().BeEquivalentTo(ApprovedAnonymousActions,
            "the set of publicly-reachable controller actions is pinned by FHQ-98 — a new " +
            "[AllowAnonymous], or a verb/route change on an approved one, must be a deliberate " +
            "decision recorded in this approved inventory");
    }

    /// <summary>
    /// Identity string pinning full type name, HTTP verb(s), and the resolved route template
    /// (controller [Route] combined with the action's HTTP-method template, with the
    /// [controller] token substituted).
    /// </summary>
    private static string ActionIdentity(MethodInfo action)
    {
        var controller = action.ReflectedType!;
        var httpMethodAttributes = action.GetCustomAttributes(inherit: true)
            .OfType<HttpMethodAttribute>()
            .ToList();

        var verbs = httpMethodAttributes
            .SelectMany(attribute => attribute.HttpMethods)
            .Distinct()
            .Order()
            .ToList();
        var verbText = verbs.Count > 0 ? string.Join(",", verbs) : "ANY";

        return $"{controller.FullName}.{action.Name} [{verbText} {RouteTemplate(controller, httpMethodAttributes)}]";
    }

    private static string RouteTemplate(Type controller, IEnumerable<HttpMethodAttribute> httpMethodAttributes)
    {
        var controllerTemplate = controller.GetCustomAttributes(inherit: true)
            .OfType<RouteAttribute>()
            .Select(route => route.Template)
            .FirstOrDefault() ?? string.Empty;

        var actionTemplate = httpMethodAttributes
            .Select(attribute => attribute.Template)
            .FirstOrDefault(template => !string.IsNullOrEmpty(template));

        var template = string.IsNullOrEmpty(actionTemplate)
            ? controllerTemplate
            : $"{controllerTemplate.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";

        var controllerToken = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;

        // Route matching is case-insensitive; the token is lower-cased purely so the pinned
        // inventory reads like the URLs the clients actually call.
        return template.Replace("[controller]", controllerToken.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static List<MethodInfo> GetControllerActions() =>
        typeof(HealthController).Assembly
            .GetTypes()
            .Where(IsRoutableController)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Distinct()
            .Where(IsAction)
            .ToList();

    /// <summary>
    /// Mirrors MVC's ControllerFeatureProvider: public, non-abstract, non-generic, not
    /// [NonController], and a ControllerBase descendant, a [Controller]-attributed type, or a
    /// "*Controller"-named POCO (which MVC would route even without a ControllerBase base).
    /// </summary>
    private static bool IsRoutableController(Type type) =>
        type.IsPublic && !type.IsAbstract && !type.ContainsGenericParameters
        && !type.IsDefined(typeof(NonControllerAttribute), inherit: true)
        && (typeof(ControllerBase).IsAssignableFrom(type)
            || type.IsDefined(typeof(ControllerAttribute), inherit: true)
            || type.Name.EndsWith("Controller", StringComparison.Ordinal));

    /// <summary>
    /// Mirrors MVC's action discovery: public instance methods, including ones inherited from
    /// base controllers, excluding [NonAction] methods (which covers every ControllerBase
    /// helper) and anything rooted on System.Object.
    /// </summary>
    private static bool IsAction(MethodInfo method) =>
        !method.IsSpecialName
        && !method.IsDefined(typeof(NonActionAttribute), inherit: true)
        && method.GetBaseDefinition().DeclaringType != typeof(object);

    private static bool HasAuthorizeMetadata(MethodInfo action) =>
        action.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any() ||
        action.ReflectedType!.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any();

    private static bool IsEffectivelyAnonymous(MethodInfo action) =>
        action.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any() ||
        action.ReflectedType!.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
}
