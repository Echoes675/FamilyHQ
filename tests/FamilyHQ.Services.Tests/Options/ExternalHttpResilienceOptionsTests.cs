using FamilyHQ.Services.Options;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Options;

public class ExternalHttpResilienceOptionsTests
{
    [Fact]
    public void Validate_Defaults_DoesNotThrow()
    {
        var options = new ExternalHttpResilienceOptions();

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Defaults_LeaveRoomForEveryInterAttemptSleep()
    {
        var options = new ExternalHttpResilienceOptions();

        // Each client Timeout is the TOTAL budget for the whole retry sequence, so it has to be
        // able to absorb the worst-case capped sleeps and still leave time for the attempts.
        var worstCaseSleeps = (options.MaxAttempts - 1) * options.MaxRetryDelay;

        options.LocationTimeout.Should().BeGreaterThan(worstCaseSleeps);
        options.GeocodingTimeout.Should().BeGreaterThan(worstCaseSleeps);
        options.WeatherTimeout.Should().BeGreaterThan(worstCaseSleeps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidMaxAttempts_Throws(int maxAttempts)
    {
        var options = new ExternalHttpResilienceOptions { MaxAttempts = maxAttempts };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.MaxAttempts)}*");
    }

    [Fact]
    public void Validate_NegativeBaseDelay_Throws()
    {
        var options = new ExternalHttpResilienceOptions { BaseDelay = TimeSpan.FromMilliseconds(-1) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.BaseDelay)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveMaxRetryDelay_Throws(int seconds)
    {
        var options = new ExternalHttpResilienceOptions { MaxRetryDelay = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.MaxRetryDelay)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(301)] // > 5 min upper bound
    public void Validate_InvalidLocationTimeout_Throws(int seconds)
    {
        var options = new ExternalHttpResilienceOptions { LocationTimeout = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.LocationTimeout)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(301)]
    public void Validate_InvalidGeocodingTimeout_Throws(int seconds)
    {
        var options = new ExternalHttpResilienceOptions { GeocodingTimeout = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.GeocodingTimeout)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(301)]
    public void Validate_InvalidWeatherTimeout_Throws(int seconds)
    {
        var options = new ExternalHttpResilienceOptions { WeatherTimeout = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.WeatherTimeout)}*");
    }

    [Fact]
    public void Validate_TimeoutSmallerThanWorstCaseSleeps_Throws()
    {
        // 3 attempts => up to 2 capped sleeps of 5s = 10s of sleeping alone; a 5s total budget
        // could never complete the configured retry sequence, so it must fail at boot.
        var options = new ExternalHttpResilienceOptions
        {
            MaxAttempts = 3,
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            WeatherTimeout = TimeSpan.FromSeconds(5)
        };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ExternalHttpResilienceOptions.WeatherTimeout)}*");
    }
}
