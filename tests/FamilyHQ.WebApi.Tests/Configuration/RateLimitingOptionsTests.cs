using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Xunit;

// Deliberately in the test-root namespace: a "FamilyHQ.WebApi.Tests.Options" namespace would
// shadow Microsoft.Extensions.Options.Options for every sibling test namespace's
// unqualified Options.Create(...) calls.
namespace FamilyHQ.WebApi.Tests;

public class RateLimitingOptionsTests
{
    [Fact]
    public void Validate_WithDefaults_DoesNotThrow()
    {
        // Arrange
        var options = new RateLimitingOptions();

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Defaults_PinTheE2ESafeLimits()
    {
        // Arrange / Act
        var options = new RateLimitingOptions();

        // Assert — sized from observed Deploy-Dev E2E traffic with >=5x headroom (FHQ-101):
        // ~181 login flows x 2 auth requests per run, 31 webhook pushes, and per-user
        // sync/weather peaks of 1/min and 3/min respectively.
        options.AuthPerIp.PermitLimit.Should().Be(300);
        options.AuthPerIp.Window.Should().Be(TimeSpan.FromMinutes(1));
        options.WebhookPerIp.PermitLimit.Should().Be(30);
        options.WebhookPerIp.Window.Should().Be(TimeSpan.FromMinutes(1));
        options.SyncTriggerPerUser.PermitLimit.Should().Be(10);
        options.SyncTriggerPerUser.Window.Should().Be(TimeSpan.FromMinutes(1));
        options.WeatherRefreshPerUser.PermitLimit.Should().Be(15);
        options.WeatherRefreshPerUser.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenPermitLimitNotPositive_ThrowsNamingThePolicy(int permitLimit)
    {
        // Arrange
        var options = new RateLimitingOptions();
        options.WebhookPerIp.PermitLimit = permitLimit;

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WebhookPerIp*PermitLimit*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Validate_WhenWindowNotPositive_ThrowsNamingThePolicy(int windowSeconds)
    {
        // Arrange
        var options = new RateLimitingOptions();
        options.AuthPerIp.Window = TimeSpan.FromSeconds(windowSeconds);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AuthPerIp*Window*");
    }

    [Fact]
    public void Validate_WhenWindowExceedsOneHour_Throws()
    {
        // Arrange
        var options = new RateLimitingOptions();
        options.SyncTriggerPerUser.Window = TimeSpan.FromHours(2);

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SyncTriggerPerUser*Window*");
    }

    [Theory]
    [InlineData("AuthPerIp")]
    [InlineData("WebhookPerIp")]
    [InlineData("SyncTriggerPerUser")]
    [InlineData("WeatherRefreshPerUser")]
    public void Validate_ChecksEveryPolicy(string policyProperty)
    {
        // Arrange — break exactly one policy and expect the error to name it.
        var options = new RateLimitingOptions();
        var policy = policyProperty switch
        {
            "AuthPerIp" => options.AuthPerIp,
            "WebhookPerIp" => options.WebhookPerIp,
            "SyncTriggerPerUser" => options.SyncTriggerPerUser,
            "WeatherRefreshPerUser" => options.WeatherRefreshPerUser,
            _ => throw new InvalidOperationException($"Unknown policy property '{policyProperty}'.")
        };
        policy.PermitLimit = 0;

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{policyProperty}*");
    }
}
