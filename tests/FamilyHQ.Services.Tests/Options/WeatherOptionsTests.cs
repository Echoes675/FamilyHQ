using FamilyHQ.Services.Options;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Options;

public class WeatherOptionsTests
{
    [Fact]
    public void Validate_Defaults_DoesNotThrow()
    {
        var options = new WeatherOptions();

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_MaxFailureBackoffBelowMinPollInterval_Throws()
    {
        var options = new WeatherOptions { MinPollIntervalMinutes = 5, MaxFailureBackoffMinutes = 4 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.MaxFailureBackoffMinutes)}*");
    }

    [Fact]
    public void Validate_MaxFailureBackoffBeyondADay_Throws()
    {
        // A backoff longer than Task.Delay can express (~49.7 days) throws inside the poll loop,
        // which then catches it and re-enters every minute forever — the exact spin FHQ-109 removed.
        var options = new WeatherOptions { MaxFailureBackoffMinutes = 1441 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.MaxFailureBackoffMinutes)}*");
    }

    [Fact]
    public void Validate_MaxFailureBackoffOfExactlyOneDay_IsAllowed()
    {
        var options = new WeatherOptions { MaxFailureBackoffMinutes = 1440 };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Validate_FailureBackoffMultiplierBelowOne_Throws()
    {
        var options = new WeatherOptions { FailureBackoffMultiplier = 0.5 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.FailureBackoffMultiplier)}*");
    }
}
