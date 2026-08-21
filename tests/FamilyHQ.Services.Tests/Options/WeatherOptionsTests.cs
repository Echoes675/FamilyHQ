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

    // FHQ-159, signed off 2026-08-17. The two numbers are the policy, so they are pinned here
    // rather than left to whatever an unconfigured deployment happens to inherit: 6 h of forecast
    // retention is 12 consecutive missed polls at the 30-minute production interval, and the
    // Current row gets 1 h because it asserts something about now.
    [Fact]
    public void ForecastStaleAfterMinutes_DefaultsToSixHours()
    {
        new WeatherOptions().ForecastStaleAfterMinutes.Should().Be(360);
    }

    [Fact]
    public void CurrentStaleAfterMinutes_DefaultsToOneHour()
    {
        new WeatherOptions().CurrentStaleAfterMinutes.Should().Be(60);
    }

    [Fact]
    public void Validate_ForecastStaleAfterMinutesBelowOne_Throws()
    {
        // Zero would hide every forecast the instant it was written — the kiosk would read as
        // permanently broken, so this must surface at boot rather than as blank weather.
        var options = new WeatherOptions { ForecastStaleAfterMinutes = 0 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.ForecastStaleAfterMinutes)}*");
    }

    [Fact]
    public void Validate_CurrentStaleAfterMinutesBelowOne_Throws()
    {
        var options = new WeatherOptions { CurrentStaleAfterMinutes = 0 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.CurrentStaleAfterMinutes)}*");
    }

    [Fact]
    public void Validate_ForecastStaleAfterMinutesBeyondADay_Throws()
    {
        // A year of "retention" is retention switched off: the kiosk would show a year-old forecast
        // rather than hiding it, which is the exact failure the window exists to prevent. Like
        // MaxFailureBackoffMinutes, the setting is bounded at both ends so a typo fails at boot.
        var options = new WeatherOptions { ForecastStaleAfterMinutes = 525600 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.ForecastStaleAfterMinutes)}*");
    }

    [Fact]
    public void Validate_CurrentStaleAfterMinutesBeyondADay_Throws()
    {
        var options = new WeatherOptions { CurrentStaleAfterMinutes = 1441 };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(WeatherOptions.CurrentStaleAfterMinutes)}*");
    }

    [Fact]
    public void Validate_RetentionWindowsOfExactlyOneDay_AreAllowed()
    {
        var options = new WeatherOptions { ForecastStaleAfterMinutes = 1440, CurrentStaleAfterMinutes = 1440 };

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }
}
