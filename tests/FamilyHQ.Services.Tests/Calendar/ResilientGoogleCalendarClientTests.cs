using System.Net;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class ResilientGoogleCalendarClientTests
{
    private static readonly CalendarEvent SampleEvent = new() { Title = "x" };

    private static (ResilientGoogleCalendarClient sut, Mock<IGoogleCalendarClient> inner, TimerArmedTimeProvider time) CreateSut(
        int maxAttempts = 3, TimeSpan? baseDelay = null, TimeSpan? cap = null)
    {
        var inner = new Mock<IGoogleCalendarClient>();
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleResilienceOptions
        {
            MaxAttempts = maxAttempts,
            BaseDelay = baseDelay ?? TimeSpan.Zero,          // 0 → exponential path is instant + deterministic
            RetryAfterInRequestCap = cap ?? TimeSpan.FromSeconds(5)
        });
        var time = new TimerArmedTimeProvider(new FakeTimeProvider());
        var sut = new ResilientGoogleCalendarClient(inner.Object, options, time, NullLogger<ResilientGoogleCalendarClient>.Instance);
        return (sut, inner, time);
    }

    private static GoogleApiException Api(HttpStatusCode status, TimeSpan? retryAfter = null)
        => new(status, "op", retryAfter);

    [Fact]
    public async Task FullPolicy_Retries5xx_ThenSucceeds()
    {
        var (sut, inner, _) = CreateSut();
        inner.SetupSequence(c => c.GetEventAsync("cal", "e", It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.InternalServerError))
            .ReturnsAsync((GoogleEventDetail?)null);

        var result = await sut.GetEventAsync("cal", "e");

        result.Should().BeNull();
        inner.Verify(c => c.GetEventAsync("cal", "e", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RejectedOnlyPolicy_DoesNotRetry5xx()
    {
        var (sut, inner, _) = CreateSut();
        inner.Setup(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.InternalServerError));

        await sut.Invoking(s => s.CreateEventAsync("cal", SampleEvent, "h"))
            .Should().ThrowAsync<GoogleApiException>();

        inner.Verify(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectedOnlyPolicy_Retries429_ThenSucceeds()
    {
        var (sut, inner, _) = CreateSut();
        inner.SetupSequence(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.TooManyRequests))
            .ReturnsAsync(SampleEvent);

        var result = await sut.CreateEventAsync("cal", SampleEvent, "h");

        result.Should().BeSameAs(SampleEvent);
        inner.Verify(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Retries403RateLimit_ForAnyPolicy()
    {
        var (sut, inner, _) = CreateSut();
        inner.SetupSequence(c => c.MoveEventAsync("s", "e", "d", It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.Forbidden))
            .ReturnsAsync("e");

        var result = await sut.MoveEventAsync("s", "e", "d");

        result.Should().Be("e");
        inner.Verify(c => c.MoveEventAsync("s", "e", "d", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExhaustsMaxAttempts_RethrowsLast()
    {
        var (sut, inner, _) = CreateSut(maxAttempts: 3);
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.ServiceUnavailable));

        await sut.Invoking(s => s.GetCalendarsAsync()).Should().ThrowAsync<GoogleApiException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task FullPolicy_DoesNotRetryOther4xx(HttpStatusCode status)
    {
        // FHQ-173 moved the "may have been processed" test into GoogleWriteOutcome, shared with the
        // split compensator. This pins that the move did not broaden what gets retried: a 4xx other
        // than 429 / rate-limit 403 was refused outright, and repeating it earns the same refusal.
        var (sut, inner, _) = CreateSut();
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(status));

        await sut.Invoking(s => s.GetCalendarsAsync()).Should().ThrowAsync<GoogleApiException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoesNotRetryReauthException()
    {
        var (sut, inner, _) = CreateSut();
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "reconnect"));

        await sut.Invoking(s => s.GetCalendarsAsync()).Should().ThrowAsync<GoogleReauthRequiredException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryAfterAboveCap_RethrowsWithoutRetrying()
    {
        var (sut, inner, _) = CreateSut(cap: TimeSpan.FromSeconds(5));
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(120)));

        await sut.Invoking(s => s.GetCalendarsAsync()).Should().ThrowAsync<GoogleApiException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // HttpClient's per-attempt timeout surfaces as TaskCanceledException wrapping TimeoutException
    // (distinct from caller cancellation, whose token is cancelled) — FHQ-91.
    private static TaskCanceledException HttpTimeout()
        => new("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException());

    [Fact]
    public async Task FullPolicy_RetriesHttpTimeout_ThenSucceeds()
    {
        var (sut, inner, _) = CreateSut();
        inner.SetupSequence(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(HttpTimeout())
            .ReturnsAsync(Array.Empty<CalendarInfo>());

        var result = await sut.GetCalendarsAsync();

        result.Should().BeEmpty();
        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RejectedOnlyPolicy_DoesNotRetryHttpTimeout()
    {
        // A timed-out create may still have been processed by Google — retrying risks duplicates.
        var (sut, inner, _) = CreateSut();
        inner.Setup(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()))
            .ThrowsAsync(HttpTimeout());

        await sut.Invoking(s => s.CreateEventAsync("cal", SampleEvent, "h"))
            .Should().ThrowAsync<TaskCanceledException>();

        inner.Verify(c => c.CreateEventAsync("cal", It.IsAny<CalendarEvent>(), "h", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HttpTimeout_ExhaustsMaxAttempts_RethrowsTaskCanceled()
    {
        var (sut, inner, _) = CreateSut(maxAttempts: 3);
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(HttpTimeout());

        await sut.Invoking(s => s.GetCalendarsAsync()).Should().ThrowAsync<TaskCanceledException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CallerCancellation_IsNotRetried()
    {
        var (sut, inner, _) = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        inner.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("canceled", new TimeoutException()));

        await sut.Invoking(s => s.GetCalendarsAsync(cts.Token)).Should().ThrowAsync<TaskCanceledException>();

        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryAfterWithinCap_RetriesAfterThatDelay()
    {
        var (sut, inner, time) = CreateSut(cap: TimeSpan.FromSeconds(5));
        inner.SetupSequence(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(Api(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(2)))
            .ReturnsAsync(Array.Empty<CalendarInfo>());

        var task = sut.GetCalendarsAsync();               // first call throws, then sleeps 2s on the fake clock
        await time.AdvanceOnNextTimerAsync(TimeSpan.FromSeconds(2)); // release the delay, once it is armed
        var result = await task;

        result.Should().BeEmpty();
        inner.Verify(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
