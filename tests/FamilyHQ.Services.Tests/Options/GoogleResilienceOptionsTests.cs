using FamilyHQ.Services.Options;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Options;

public class GoogleResilienceOptionsTests
{
    [Fact]
    public void Validate_Defaults_DoesNotThrow()
    {
        var options = new GoogleResilienceOptions();

        options.Invoking(o => o.Validate()).Should().NotThrow();
    }

    [Fact]
    public void Defaults_KeepWorstCaseWallTime_UnderThreeMinutes()
    {
        var options = new GoogleResilienceOptions();

        // Worst case per operation: every attempt burns the full per-attempt timeout, plus the
        // capped inter-attempt sleeps. Must stay under 3 minutes (and well under the sync
        // worker's 5-minute OrphanRecoveryThreshold).
        var worstCase = options.MaxAttempts * options.CalendarTimeout
            + (options.MaxAttempts - 1) * options.RetryAfterInRequestCap;

        worstCase.Should().BeLessThan(TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(301)] // > 5 min upper bound
    public void Validate_InvalidAuthTimeout_Throws(int seconds)
    {
        var options = new GoogleResilienceOptions { AuthTimeout = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GoogleResilienceOptions.AuthTimeout)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-45)]
    [InlineData(301)] // > 5 min upper bound
    public void Validate_InvalidCalendarTimeout_Throws(int seconds)
    {
        var options = new GoogleResilienceOptions { CalendarTimeout = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GoogleResilienceOptions.CalendarTimeout)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidMaxAttempts_Throws(int maxAttempts)
    {
        var options = new GoogleResilienceOptions { MaxAttempts = maxAttempts };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GoogleResilienceOptions.MaxAttempts)}*");
    }

    [Fact]
    public void Validate_NegativeBaseDelay_Throws()
    {
        var options = new GoogleResilienceOptions { BaseDelay = TimeSpan.FromMilliseconds(-1) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GoogleResilienceOptions.BaseDelay)}*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveRetryAfterInRequestCap_Throws(int seconds)
    {
        var options = new GoogleResilienceOptions { RetryAfterInRequestCap = TimeSpan.FromSeconds(seconds) };

        options.Invoking(o => o.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GoogleResilienceOptions.RetryAfterInRequestCap)}*");
    }
}
