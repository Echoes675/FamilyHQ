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
/// FHQ-114. Every test runs on a <see cref="FakeTimeProvider"/> with <c>BaseDelay = 0</c>, so the
/// exponential path is instant and no test ever waits out a real backoff.
/// </summary>
public class TransientHttpRetryHandlerTests
{
    private const string Url = "https://example.test/json";

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
    public async Task SendAsync_IpApiReturns429ThenSuccess_RetriesAndSucceeds()
    {
        var (client, inner, _) = CreateClient(new StubHandler(
            () => Status(HttpStatusCode.TooManyRequests),
            IpApiSuccess));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("success");
        inner.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
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
        // ip-api.com signals its 45-req/min rate-limit window with X-Ttl (seconds), never Retry-After;
        // an X-Ttl longer than the cap must stop the retry instead of holding the request open.
        var (client, inner, _) = CreateClient(
            new StubHandler(
                () => WithXTtl(Status(HttpStatusCode.TooManyRequests), 47),
                () => Status(HttpStatusCode.OK)),
            maxRetryDelay: TimeSpan.FromSeconds(5));

        var response = await client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        inner.Calls.Should().Be(1);
    }

    // ---- Delay computation (internal seam: exact durations, no clock races) ----
    [Fact]
    public void ComputeRetryDelay_RetryAfterDelta_IsHonoured()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var delay = handler.ComputeRetryDelay(
            WithRetryAfter(Status(HttpStatusCode.TooManyRequests), TimeSpan.FromSeconds(3)), attempt: 1);

        delay.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ComputeRetryDelay_RetryAfterHttpDate_IsHonoured()
    {
        var (handler, time) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));
        var response = Status(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(time.GetUtcNow().AddSeconds(4));

        var delay = handler.ComputeRetryDelay(response, attempt: 1);

        delay.Should().BeCloseTo(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(1100));
    }

    [Fact]
    public void ComputeRetryDelay_XTtl_IsHonouredWhenRetryAfterAbsent()
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var delay = handler.ComputeRetryDelay(
            WithXTtl(Status(HttpStatusCode.TooManyRequests), 2), attempt: 1);

        delay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(1, 500, 1000)]
    [InlineData(2, 1000, 2000)]
    [InlineData(3, 2000, 4000)]
    public void ComputeRetryDelay_NoServerHint_UsesExponentialFullJitter(int attempt, int minMs, int maxMs)
    {
        var (handler, _) = CreateHandler(baseDelay: TimeSpan.FromMilliseconds(500));

        var delay = handler.ComputeRetryDelay(Status(HttpStatusCode.ServiceUnavailable), attempt);

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
