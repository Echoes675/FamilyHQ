using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

// Deliberately in the test-root namespace, matching JwtSessionOptionsTests: a
// "FamilyHQ.WebApi.Tests.Configuration" namespace would shadow types for sibling namespaces.
namespace FamilyHQ.WebApi.Tests;

public class AuthorizationPolicyConfigurationTests
{
    [Fact]
    public void AddFallbackAuthorizationPolicy_WhenOptionsResolved_FallbackPolicyIsConfigured()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFallbackAuthorizationPolicy();

        // Assert
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        options.FallbackPolicy.Should().NotBeNull(
            "FHQ-98: endpoints without authorization metadata must fall back to a deny-by-default policy");
    }

    [Fact]
    public void AddFallbackAuthorizationPolicy_WhenOptionsResolved_FallbackPolicyRequiresAuthenticatedUser()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFallbackAuthorizationPolicy();

        // Assert
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        options.FallbackPolicy!.Requirements.Should().ContainSingle()
            .Which.Should().BeOfType<DenyAnonymousAuthorizationRequirement>(
                "the fallback policy must require an authenticated user and nothing weaker");
    }
}
