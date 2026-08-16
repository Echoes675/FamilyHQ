using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

// Deliberately in the test-root namespace: a "FamilyHQ.WebApi.Tests.Options" namespace would
// shadow Microsoft.Extensions.Options.Options for every sibling test namespace's
// unqualified Options.Create(...) calls.
namespace FamilyHQ.WebApi.Tests;

public class RateLimitingConfigurationTests
{
    private const string TestIp = "203.0.113.7";
    private const string TestSub = "108234567890123456789";

    // ── Partition key resolution ─────────────────────────────────────────────

    [Fact]
    public void ResolveIpPartitionKey_WithRemoteIp_ReturnsIpKey()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);

        // Act
        var key = RateLimitingConfiguration.ResolveIpPartitionKey(httpContext);

        // Assert
        key.Should().Be($"ip:{TestIp}");
    }

    [Fact]
    public void ResolveIpPartitionKey_WithNullRemoteIp_ReturnsSharedUnknownKey()
    {
        // Arrange — RemoteIpAddress is null for some in-process/test transports; all such
        // requests deliberately share one partition rather than escaping limiting entirely.
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = null;

        // Act
        var key = RateLimitingConfiguration.ResolveIpPartitionKey(httpContext);

        // Assert
        key.Should().Be(RateLimitingConfiguration.UnknownIpPartitionKey);
    }

    [Fact]
    public void ResolveUserPartitionKey_WithSubClaim_ReturnsUserKey()
    {
        // Arrange — MapInboundClaims=false in Program.cs keeps the raw "sub" claim name,
        // mirroring how CurrentUserService reads the authenticated user.
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", TestSub) }, authenticationType: "TestAuth"));

        // Act
        var key = RateLimitingConfiguration.ResolveUserPartitionKey(httpContext);

        // Assert
        key.Should().Be($"user:{TestSub}");
    }

    [Fact]
    public void ResolveUserPartitionKey_WithoutSubClaim_FallsBackToIpKey()
    {
        // Arrange — unauthenticated hits on per-user endpoints would 401 anyway, but the
        // limiter runs regardless of auth outcome, so they must land in the IP partition.
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);

        // Act
        var key = RateLimitingConfiguration.ResolveUserPartitionKey(httpContext);

        // Assert
        key.Should().Be($"ip:{TestIp}");
    }

    [Fact]
    public void ResolveUserPartitionKey_UnauthenticatedAndNoIp_ReturnsSharedUnknownKey()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = null;

        // Act
        var key = RateLimitingConfiguration.ResolveUserPartitionKey(httpContext);

        // Assert
        key.Should().Be(RateLimitingConfiguration.UnknownIpPartitionKey);
    }

    // ── Policy wiring (limits + partition per policy) ────────────────────────
    // The framework's PolicyMap is internal, so registration goes through these two seams and
    // the seams are pinned here: cross-wiring a policy's limits or partitioning (e.g. giving the
    // auth policy the webhook limit, or partitioning a per-user policy by IP) would otherwise be
    // invisible to every other test in this file.

    [Theory]
    [InlineData(RateLimitPolicies.AuthPerIp, 11)]
    [InlineData(RateLimitPolicies.WebhookPerIp, 22)]
    [InlineData(RateLimitPolicies.SyncTriggerPerUser, 33)]
    [InlineData(RateLimitPolicies.WeatherRefreshPerUser, 44)]
    public void ResolveOptionsForPolicy_KnownPolicy_ReturnsThatPolicysLimits(
        string policyName, int expectedPermitLimit)
    {
        // Arrange — distinct limits so a wrong mapping cannot pass by coincidence.
        var options = new RateLimitingOptions();
        options.AuthPerIp.PermitLimit = 11;
        options.WebhookPerIp.PermitLimit = 22;
        options.SyncTriggerPerUser.PermitLimit = 33;
        options.WeatherRefreshPerUser.PermitLimit = 44;

        // Act
        var resolved = RateLimitingConfiguration.ResolveOptionsForPolicy(policyName, options);

        // Assert
        resolved.Should().NotBeNull();
        resolved!.PermitLimit.Should().Be(expectedPermitLimit);
    }

    [Fact]
    public void ResolveOptionsForPolicy_UnknownPolicy_ReturnsNull()
    {
        // Arrange
        var options = new RateLimitingOptions();

        // Act
        var resolved = RateLimitingConfiguration.ResolveOptionsForPolicy("no-such-policy", options);

        // Assert
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveOptionsForPolicy_EveryRegisteredPolicy_HasLimits()
    {
        // Arrange — registration throws at boot for a policy with no limits, so this is the
        // cheap guard that adding a policy name without limits fails here first.
        var options = new RateLimitingOptions();

        // Act
        var resolved = RateLimitPolicies.All
            .Select(policy => RateLimitingConfiguration.ResolveOptionsForPolicy(policy, options));

        // Assert
        resolved.Should().OnlyContain(policyOptions => policyOptions != null);
    }

    [Theory]
    [InlineData(RateLimitPolicies.SyncTriggerPerUser)]
    [InlineData(RateLimitPolicies.WeatherRefreshPerUser)]
    public void ResolvePartitionKeyForPolicy_PerUserPolicy_PartitionsBySubClaim(string policyName)
    {
        // Arrange
        var httpContext = CreateAuthenticatedContext();

        // Act
        var key = RateLimitingConfiguration.ResolvePartitionKeyForPolicy(policyName, httpContext);

        // Assert
        key.Should().Be($"user:{TestSub}");
    }

    [Theory]
    [InlineData(RateLimitPolicies.AuthPerIp)]
    [InlineData(RateLimitPolicies.WebhookPerIp)]
    public void ResolvePartitionKeyForPolicy_PerIpPolicy_PartitionsByIpEvenWhenAuthenticated(
        string policyName)
    {
        // Arrange — an authenticated caller must still be limited per IP on these policies.
        var httpContext = CreateAuthenticatedContext();

        // Act
        var key = RateLimitingConfiguration.ResolvePartitionKeyForPolicy(policyName, httpContext);

        // Assert
        key.Should().Be($"ip:{TestIp}");
    }

    // ── Retry-After derivation ───────────────────────────────────────────────

    [Fact]
    public void DeriveRetryAfterSeconds_WithLeaseMetadata_ReturnsCeilingOfMetadataSeconds()
    {
        // Arrange
        var lease = new TestRateLimitLease(TimeSpan.FromSeconds(17.3));

        // Act
        var seconds = RateLimitingConfiguration.DeriveRetryAfterSeconds(lease, TimeSpan.FromMinutes(1));

        // Assert
        seconds.Should().Be(18);
    }

    [Fact]
    public void DeriveRetryAfterSeconds_WithoutMetadata_FallsBackToWindowSeconds()
    {
        // Arrange
        var lease = new TestRateLimitLease(retryAfter: null);

        // Act
        var seconds = RateLimitingConfiguration.DeriveRetryAfterSeconds(lease, TimeSpan.FromSeconds(45));

        // Assert
        seconds.Should().Be(45);
    }

    [Fact]
    public void DeriveRetryAfterSeconds_WithZeroMetadata_ReturnsOneSecondNotTheWindow()
    {
        // Arrange — at the window edge the lease reports zero; the honest answer is "retry now",
        // so the fallback window must NOT be substituted for it.
        var lease = new TestRateLimitLease(TimeSpan.Zero);

        // Act
        var seconds = RateLimitingConfiguration.DeriveRetryAfterSeconds(lease, TimeSpan.FromMinutes(1));

        // Assert
        seconds.Should().Be(1);
    }

    [Fact]
    public void DeriveRetryAfterSeconds_WithSubSecondMetadata_ReturnsAtLeastOneSecond()
    {
        // Arrange — a Retry-After of 0 would invite an immediate client retry loop.
        var lease = new TestRateLimitLease(TimeSpan.FromMilliseconds(200));

        // Act
        var seconds = RateLimitingConfiguration.DeriveRetryAfterSeconds(lease, TimeSpan.FromMinutes(1));

        // Assert
        seconds.Should().Be(1);
    }

    // ── Policy window resolution ─────────────────────────────────────────────

    [Theory]
    [InlineData(RateLimitPolicies.AuthPerIp, 100)]
    [InlineData(RateLimitPolicies.WebhookPerIp, 200)]
    [InlineData(RateLimitPolicies.SyncTriggerPerUser, 300)]
    [InlineData(RateLimitPolicies.WeatherRefreshPerUser, 400)]
    public void ResolveWindowForPolicy_KnownPolicy_ReturnsThatPolicysConfiguredWindow(
        string policyName, int expectedSeconds)
    {
        // Arrange — distinct windows so a wrong mapping cannot pass by coincidence.
        var options = new RateLimitingOptions();
        options.AuthPerIp.Window = TimeSpan.FromSeconds(100);
        options.WebhookPerIp.Window = TimeSpan.FromSeconds(200);
        options.SyncTriggerPerUser.Window = TimeSpan.FromSeconds(300);
        options.WeatherRefreshPerUser.Window = TimeSpan.FromSeconds(400);

        // Act
        var window = RateLimitingConfiguration.ResolveWindowForPolicy(policyName, options);

        // Assert
        window.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void ResolveWindowForPolicy_UnknownPolicy_ReturnsOneMinuteDefault()
    {
        // Arrange
        var options = new RateLimitingOptions();

        // Act
        var window = RateLimitingConfiguration.ResolveWindowForPolicy("no-such-policy", options);

        // Assert
        window.Should().Be(TimeSpan.FromMinutes(1));
    }

    // ── Registration pinning ─────────────────────────────────────────────────

    [Fact]
    public void AddFamilyHqRateLimiting_Rejects429WithOnRejectedAndNoGlobalLimiter()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFamilyHqRateLimiting(new RateLimitingOptions());
        using var provider = services.BuildServiceProvider();
        var limiterOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        // Assert — NO GlobalLimiter: the kiosk polls other endpoints continuously and the
        // SignalR hub must never be limited (reconnect storms after an outage are legitimate).
        limiterOptions.RejectionStatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        limiterOptions.OnRejected.Should().NotBeNull();
        limiterOptions.GlobalLimiter.Should().BeNull();
    }

    // ── OnRejected behaviour ─────────────────────────────────────────────────

    [Fact]
    public async Task OnRejected_SetsRetryAfterHeaderWritesProblemDetailsAndLogsWarning()
    {
        // Arrange
        var loggerMock = CreateLoggerMock();
        var httpContext = CreateRejectionHttpContext(loggerMock, RateLimitPolicies.AuthPerIp);
        var onRejected = ResolveOnRejected(new RateLimitingOptions());

        // Act
        await onRejected(
            new OnRejectedContext { HttpContext = httpContext, Lease = new TestRateLimitLease(TimeSpan.FromSeconds(17.3)) },
            CancellationToken.None);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be("18");
        httpContext.Response.ContentType.Should().StartWith("application/problem+json");

        using var body = ParseResponseBody(httpContext);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        body.RootElement.GetProperty("title").GetString().Should().Be("Too Many Requests");
        // Correlatable in Seq like every other ProblemDetails response (FHQ-39 writes this via
        // the framework's writer; the rejection path writes it directly, so it adds it itself).
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(RateLimitPolicies.AuthPerIp)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task OnRejected_WithoutLeaseMetadata_DerivesRetryAfterFromThePolicysWindow()
    {
        // Arrange
        var options = new RateLimitingOptions();
        options.WebhookPerIp.Window = TimeSpan.FromSeconds(20);
        var httpContext = CreateRejectionHttpContext(CreateLoggerMock(), RateLimitPolicies.WebhookPerIp);
        var onRejected = ResolveOnRejected(options);

        // Act
        await onRejected(
            new OnRejectedContext { HttpContext = httpContext, Lease = new TestRateLimitLease(retryAfter: null) },
            CancellationToken.None);

        // Assert
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be("20");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Mock<ILogger> CreateLoggerMock()
    {
        var loggerMock = new Mock<ILogger>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return loggerMock;
    }

    private static DefaultHttpContext CreateRejectionHttpContext(Mock<ILogger> loggerMock, string policyName)
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        var requestServices = new ServiceCollection()
            .AddSingleton(loggerFactoryMock.Object)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices
        };
        httpContext.Response.Body = new MemoryStream();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);
        httpContext.SetEndpoint(new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(new EnableRateLimitingAttribute(policyName)),
            displayName: "test-endpoint"));
        return httpContext;
    }

    private static Func<OnRejectedContext, CancellationToken, ValueTask> ResolveOnRejected(
        RateLimitingOptions options)
    {
        var services = new ServiceCollection();
        services.AddFamilyHqRateLimiting(options);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected!;
    }

    private static DefaultHttpContext CreateAuthenticatedContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", TestSub) }, authenticationType: "TestAuth"));
        return httpContext;
    }

    private static JsonDocument ParseResponseBody(DefaultHttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        return JsonDocument.Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Minimal fake lease: <see cref="RateLimitLease.TryGetMetadata{T}(MetadataName{T}, out T)"/>
    /// routes through the abstract string overload overridden here.
    /// </summary>
    private sealed class TestRateLimitLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? Array.Empty<string>() : new[] { MetadataName.RetryAfter.Name };

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is { } value && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
