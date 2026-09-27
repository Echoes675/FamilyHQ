using FamilyHQ.Services.Calendar;
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithANonPositiveRenewalPollInterval_Throws(int hours)
    {
        // FHQ-213: the renewal loop sleeps this between passes, so a non-positive value is a spin loop
        // hammering the database and Google for as long as the process lives.
        var options = ValidAddressWith(TimeSpan.FromHours(hours));

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(SyncOptions.WebhookRenewalPollInterval)}*");
    }

    [Theory]
    [InlineData(24)]  // exactly the window: a pass could step from just outside it to just past expiry
    [InlineData(144)] // the interval the old design used, and the reason FHQ-213 happened
    public void Validate_WithARenewalPollIntervalThatCanStepOverTheRenewalWindow_Throws(int hours)
    {
        // FHQ-213: channels are only re-registered inside their final RenewalWindow. Polling at least
        // that slowly means some restart offset never lands inside it, and the channel lapses silently.
        var options = ValidAddressWith(TimeSpan.FromHours(hours));

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(SyncOptions.WebhookRenewalPollInterval)}*");
    }

    [Fact]
    public void Validate_WithTheDefaultRenewalPollInterval_DoesNotThrow()
    {
        var options = new SyncOptions { WebhookRegistrationEnabled = true, WebhookBaseUrl = "https://familyhq.api.example.com" };

        options.WebhookRenewalPollInterval.Should().BeLessThan(WebhookRegistrationService.RenewalWindow,
            "the default has to satisfy its own rule, or no deployment boots");
        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_WithOnlyTheLegacyRenewalIntervalConfigured_StillBootsOnTheNewDefault()
    {
        // FHQ-213: the deployed env credentials still set Sync__WebhookRenewalInterval=6.00:00:00. An
        // environment that sets only the old key must boot unchanged and poll on the new cadence;
        // WebhookRenewalService warns that the key is ignored.
        var options = new SyncOptions
        {
            WebhookRegistrationEnabled = true,
            WebhookBaseUrl = "https://familyhq.api.example.com",
            WebhookRenewalInterval = TimeSpan.FromDays(6)
        };

        options.Invoking(o => o.Validate()).Should().NotThrow();
        options.WebhookRenewalPollInterval.Should().Be(TimeSpan.FromHours(1));
    }

    private static SyncOptions ValidAddressWith(TimeSpan renewalPollInterval) =>
        new()
        {
            WebhookRegistrationEnabled = true,
            WebhookBaseUrl = "https://familyhq.api.example.com",
            WebhookRenewalPollInterval = renewalPollInterval
        };
}
