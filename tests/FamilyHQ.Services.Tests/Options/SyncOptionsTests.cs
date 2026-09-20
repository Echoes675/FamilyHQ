using FamilyHQ.Services.Options;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Options;

/// <summary>
/// FHQ-196. The registration address is the one setting whose absence is invisible at runtime:
/// webhook registration simply logs a warning per calendar and carries on, so a deployment that
/// forgot it looks healthy while push notifications never arrive.
/// </summary>
public class SyncOptionsTests
{
    [Fact]
    public void Validate_RegistrationEnabledWithNoWebhookBaseUrl_Throws()
    {
        var options = new SyncOptions { WebhookRegistrationEnabled = true, WebhookBaseUrl = null };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(SyncOptions.WebhookBaseUrl)}*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api/sync/webhook")]
    [InlineData("familyhq.example.com")]
    [InlineData("webapi:8080")] // parses as a URI whose SCHEME is "webapi" — absolute, and useless
    public void Validate_RegistrationEnabledWithAnAddressThatIsNotAnAbsoluteHttpUrl_Throws(string baseUrl)
    {
        var options = new SyncOptions { WebhookRegistrationEnabled = true, WebhookBaseUrl = baseUrl };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(SyncOptions.WebhookBaseUrl)}*");
    }

    [Theory]
    [InlineData("http://webapi:8080")] // dev and staging, against the Simulator — http on purpose
    [InlineData("https://familyhq.api.example.com")]
    [InlineData("https://relay.example.com/h/a-route-key")]
    public void Validate_RegistrationEnabledWithAnAbsoluteAddress_DoesNotThrow(string baseUrl)
    {
        var options = new SyncOptions { WebhookRegistrationEnabled = true, WebhookBaseUrl = baseUrl };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_RegistrationDisabled_IgnoresAMissingWebhookBaseUrl()
    {
        // Nothing will be registered, so there is no address to be wrong about.
        var options = new SyncOptions { WebhookRegistrationEnabled = false, WebhookBaseUrl = null };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_AnInvalidAddressCarryingARouteKey_KeepsTheKeyOutOfTheMessage()
    {
        // An exception message is a log sink: it reaches Seq through whatever logs the startup
        // failure. The route key authorises posting notifications to FamilyHQ.
        var options = new SyncOptions
        {
            WebhookRegistrationEnabled = true,
            WebhookBaseUrl = "relay.example.com/h/s3cr3t-route-key"
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain("s3cr3t-route-key");
    }
}
