using System.Net;
using System.Net.Http.Headers;
using FamilyHQ.Services.Http;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FamilyHQ.Services.Tests.Http;

/// <summary>
/// FHQ-114. Tests either run with <c>BaseDelay = 0</c> so the exponential path is instant, or park
/// the handler on the <see cref="FakeTimeProvider"/> and release it by advancing virtual time — no
/// test ever waits out a real backoff.
/// </summary>
public class TransientHttpRetryHandlerTests
{
    private const string Url = "https://example.test/json";

    /// <summary>Real time allowed for the handler to reach its (virtual) sleep. It does no I/O, so this is ~1000x headroom.</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(50);

    private static (HttpClient Client, StubHandler Inner, FakeTimeProvider Time) CreateClient(
        StubHandler inner,
        int maxAttempts = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        var (handler, time) = CreateHandler(maxAttempts, baseDelay, maxRetryDelay);
        handler.InnerHandler = inner;
        return (new HttpClient(handler), inner, time);
    }

    private static (TransientHttpRetryHandler Handler, FakeTimeProvider Time) CreateHandler(
        int maxAttempts = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ExternalHttpResilienceOptions
        {
            MaxAttempts = maxAttempts,
            BaseDelay = baseDelay ?? TimeSpan.Zero,
            MaxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(5)
        });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        return (new TransientHttpRetryHandler(options, time, NullLogger<TransientHttpRetryHandler>.Instance), time);
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage IpApiSuccess()
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"success","city":"London","regionName":"England","country":"UK","lat":51.5,"lon":-0.12}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };

    // ---- FHQ-114 AC3: a single 429 from ip-api is retried and resolves on the second attempt ----
    [Fact]
    public async Task SendAsync_IpApi429WithXTtl_SleepsOnTheInjectedClockThenSucceeds()
    {
        // ip-api's 429 always carries X-Ttl (seconds until its rate-limit window resets). This also
        // pins that the wait happens on the INJECTED clock: nothing completes until virtual time moves.
        var (client, inner, time) = CreateClient(
            new StubHandler(
                () => WithXTtl(Status(HttpStatusCode.TooManyRequests), 3),
                IpApiSuccess),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var task = client.GetAsync(Url);
        await Task.Delay(SettleWindow);

        task.IsCompleted.Should().BeFalse("the handler must be asleep on the injected clock, not the wall clock");
        inner.Calls.Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(3));
        var response = await task.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("success");
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_RetryAfterWithinCap_SleepsForExactlyThatLong()
    {
        var (client, inner, time) = CreateClient(
            new StubHandler(
                () => WithRetryAfter(Status(HttpStatusCode.ServiceUnavailable), TimeSpan.FromSeconds(3)),
                () => Status(HttpStatusCode.OK)),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var task = client.GetAsync(Url);
        await Task.Delay(SettleWindow);

        task.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(SettleWindow);
        task.IsCompleted.Should().BeFalse("2s is short of the 3s the server asked for");

        time.Advance(TimeSpan.FromSeconds(1));
        var response = await task.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task SendAsync_TransientStatus_IsRetried(HttpStatusCode transient)
    {
        var (client, inner, _) = CreateClient(new StubHandler(
            () => Status(transient),
            () => Status(HttpStatusCode.OK)));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task SendAsync_NonTransientStatus_IsNotRetried(HttpStatusCode permanent)
    {
        var (client, inner, _) = CreateClient(new StubHandler(() => Status(permanent)));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(permanent);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_429WithoutRetryHint_IsSurfacedForCallerLevelBackoff()
    {
        // Open-Meteo's 429 shape: {"error":true,"reason":"Minutely API request limit exceeded"} and no
        // Retry-After. Replaying it in-request would spend more of the same exhausted quota during
        // exactly the overload FHQ-109's poll backoff exists to end.
        var (client, inner, _) = CreateClient(new StubHandler(
            () => Status(HttpStatusCode.TooManyRequests),
            () => Status(HttpStatusCode.OK)));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_XTtlOnNon429Response_IsIgnoredAndTheRequestStillRetries()
    {
        // X-Ttl is ip-api's rate-limit WINDOW counter (paired with X-Rl, "requests remaining"), so it
        // ships on non-throttled responses too. Treating it as a wait instruction on a plain 5xx would
        // make the retry inert for the very client this handler was built around.
        var (client, inner, _) = CreateClient(
            new StubHandler(
                () => WithXTtl(Status(HttpStatusCode.ServiceUnavailable), 47),
                () => Status(HttpStatusCode.OK)),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_IsRetried()
    {
        var (client, inner, _) = CreateClient(new StubHandler(
            () => throw new HttpRequestException("connection refused"),
            () => Status(HttpStatusCode.OK)));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task SendAsync_NetworkFailureBackoffAboveCap_SurfacesImmediately()
    {
        // The exception path must respect the same ceiling as the response path, or the client's total
        // budget (the whole point of not having a per-attempt timeout) can be blown from the catch block.
        var (client, inner, _) = CreateClient(
            new StubHandler(() => throw new HttpRequestException("connection refused")),
            baseDelay: TimeSpan.FromSeconds(10),
            maxRetryDelay: TimeSpan.FromSeconds(1));

        // WaitAsync so an uncapped sleep fails the test instead of hanging it: the fake clock is
        // never advanced here, so a 10s virtual sleep would otherwise never complete.
        await client.Invoking(c => c.GetAsync(Url).WaitAsync(TimeSpan.FromSeconds(2)))
            .Should().ThrowAsync<HttpRequestException>();

        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_PersistentTransientStatus_StopsAtMaxAttemptsAndSurfacesLastResponse()
    {
        var (client, inner, _) = CreateClient(
            new StubHandler(() => Status(HttpStatusCode.ServiceUnavailable)),
            maxAttempts: 3);

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_PersistentNetworkFailure_StopsAtMaxAttemptsAndThrows()
    {
        var (client, inner, _) = CreateClient(
            new StubHandler(() => throw new HttpRequestException("connection refused")),
            maxAttempts: 3);

        await client.Invoking(c => c.GetAsync(Url)).Should().ThrowAsync<HttpRequestException>();

        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_NonIdempotentMethod_IsNotRetried()
    {
        // A POST may have been processed upstream even when the response says 5xx — never replay it.
        var (client, inner, _) = CreateClient(new StubHandler(() => Status(HttpStatusCode.ServiceUnavailable)));

        var response = await client.PostAsync(Url, new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_RetryAfterAboveCap_SurfacesResponseWithoutRetrying()
    {
        var (client, inner, _) = CreateClient(
            new StubHandler(
                () => WithRetryAfter(Status(HttpStatusCode.TooManyRequests), TimeSpan.FromMinutes(2)),
                () => Status(HttpStatusCode.OK)),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_IpApiXTtlAboveCap_SurfacesResponseWithoutRetrying()
    {
        var (client, inner, _) = CreateClient(
            new StubHandler(
                () => WithXTtl(Status(HttpStatusCode.TooManyRequests), 47),
                () => Status(HttpStatusCode.OK)),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Calls.Should().Be(1);
    }

    // ---- Server-hint parsing ----
    [Fact]
    public void GetServerRetryHint_RetryAfterDelta_IsHonoured()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var hint = handler.GetServerRetryHint(
            WithRetryAfter(Status(HttpStatusCode.TooManyRequests), TimeSpan.FromSeconds(3)));

        hint.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void GetServerRetryHint_RetryAfterHttpDate_IsHonoured()
    {
        var (handler, time) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));
        var response = Status(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(time.GetUtcNow().AddSeconds(4));

        var hint = handler.GetServerRetryHint(response);

        hint.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(1100));
    }

    [Fact]
    public void GetServerRetryHint_XTtlOn429_IsHonouredWhenRetryAfterAbsent()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var hint = handler.GetServerRetryHint(WithXTtl(Status(HttpStatusCode.TooManyRequests), 2));

        hint.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetServerRetryHint_XTtlOnNon429_IsIgnored()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var hint = handler.GetServerRetryHint(WithXTtl(Status(HttpStatusCode.ServiceUnavailable), 47));

        hint.Should().BeNull("X-Ttl only means 'wait' on a throttled response");
    }

    [Fact]
    public void GetServerRetryHint_NoHeaders_IsNull()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        handler.GetServerRetryHint(Status(HttpStatusCode.ServiceUnavailable)).Should().BeNull();
    }

    [Theory]
    [InlineData(1, 500, 1000)]
    [InlineData(2, 1000, 2000)]
    [InlineData(3, 2000, 4000)]
    public void ComputeExponentialDelay_DoublesPerAttemptWithinJitterBounds(int attempt, int minMs, int maxMs)
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var delay = handler.ComputeExponentialDelay(attempt);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(minMs));
        delay.Should().BeLessThan(TimeSpan.FromMilliseconds(maxMs));
    }

    private static HttpResponseMessage WithRetryAfter(HttpResponseMessage response, TimeSpan delta)
    {
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        return response;
    }

    private static HttpResponseMessage WithXTtl(HttpResponseMessage response, int seconds)
    {
        response.Headers.TryAddWithoutValidation("X-Ttl", seconds.ToString());
        return response;
    }

    /// <summary>Returns each queued response in turn; the last one repeats for every further attempt.</summary>
    private sealed class StubHandler(params Func<HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var step = steps[Math.Min(Calls, steps.Length - 1)];
            Calls++;
            return Task.FromResult(step());
        }
    }
}
