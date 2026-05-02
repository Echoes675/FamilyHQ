using System.Net;
using System.Text;
using FamilyHQ.WebUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Moq;

namespace FamilyHQ.WebUi.Tests.Services;

public class VersionServiceTests
{
    [Fact]
    public void Constructor_ReadsClientVersion_FromOwnAssembly()
    {
        // Arrange / Act
        var sut = CreateSut();

        // Assert
        sut.ClientVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InitializeAsync_PopulatesServerVersion_FromHealthEndpoint()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.RespondingWith(
            HttpStatusCode.OK,
            """{"status":"healthy","service":"webapi","version":"1.0.5","timestamp":"2026-05-01T00:00:00+00:00"}""");

        var sut = CreateSut(handler: handler);

        // Act
        await sut.InitializeAsync();

        // Assert
        sut.ServerVersion.Should().Be("1.0.5");
    }

    [Fact]
    public async Task InitializeAsync_OnHttpFailure_DoesNotThrow_LeavesServerVersionNull()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("network down"));
        var sut = CreateSut(handler: handler);

        // Act
        Func<Task> act = () => sut.InitializeAsync();

        // Assert
        await act.Should().NotThrowAsync();
        sut.ServerVersion.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenServerVersionMatchesClient_DoesNotRaiseUpdateAvailable()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.RespondingWithFunc((_, _) =>
            BuildHealthResponse(version: GetClientVersionCore()));

        var sut = CreateSut(handler: handler);
        var raised = 0;
        sut.UpdateAvailable += () => raised++;

        // Act
        await sut.CheckAsync();

        // Assert
        raised.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_WhenServerVersionDiffersOnlyInBuildMetadata_DoesNotRaiseUpdateAvailable()
    {
        // Arrange — server reports same SemVer core, only differing in +build metadata
        var serverVersion = $"{GetClientVersionCore()}+commitabc";
        using var handler = FakeHttpMessageHandler.RespondingWithFunc((_, _) =>
            BuildHealthResponse(version: serverVersion));

        var sut = CreateSut(handler: handler);
        var raised = 0;
        sut.UpdateAvailable += () => raised++;

        // Act
        await sut.CheckAsync();

        // Assert
        raised.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_WhenServerVersionDiffersInPatch_RaisesUpdateAvailableExactlyOnce()
    {
        // Arrange — bump the patch number of the client core to construct a guaranteed-different version
        var serverVersion = BumpPatch(GetClientVersionCore());
        using var handler = FakeHttpMessageHandler.RespondingWithFunc((_, _) =>
            BuildHealthResponse(version: serverVersion));

        var fakeTime = new FakeTimeProvider();
        var jsRuntime = new RecordingJsRuntime();
        var sut = CreateSut(handler: handler, jsRuntime: jsRuntime, timeProvider: fakeTime);
        var raised = 0;
        sut.UpdateAvailable += () => raised++;

        // Act — kick off the check; it will be parked at the 5s reload delay
        var checkTask = sut.CheckAsync();
        fakeTime.Advance(TimeSpan.FromSeconds(6));
        await checkTask;

        // Assert
        raised.Should().Be(1);
    }

    [Fact]
    public async Task CheckAsync_AfterUpdateAvailableFires_FurtherCallsDoNotRefire()
    {
        // Arrange — same response on every call, mismatch every time
        var serverVersion = BumpPatch(GetClientVersionCore());
        using var handler = FakeHttpMessageHandler.RespondingWithFunc((_, _) =>
            BuildHealthResponse(version: serverVersion));

        var fakeTime = new FakeTimeProvider();
        var jsRuntime = new RecordingJsRuntime();
        var sut = CreateSut(handler: handler, jsRuntime: jsRuntime, timeProvider: fakeTime);
        var raised = 0;
        sut.UpdateAvailable += () => raised++;

        // Act — first call triggers the update flow; advance time so it completes
        var first = sut.CheckAsync();
        fakeTime.Advance(TimeSpan.FromSeconds(6));
        await first;

        await sut.CheckAsync();
        await sut.CheckAsync();

        // Assert — idempotent: only one fire
        raised.Should().Be(1);
    }

    [Fact]
    public async Task CheckAsync_WhenMismatch_InvokesLocationReload_AfterFiveSeconds()
    {
        // Arrange
        var serverVersion = BumpPatch(GetClientVersionCore());
        using var handler = FakeHttpMessageHandler.RespondingWithFunc((_, _) =>
            BuildHealthResponse(version: serverVersion));

        var fakeTime = new FakeTimeProvider();
        var jsRuntime = new RecordingJsRuntime();

        var sut = CreateSut(handler: handler, jsRuntime: jsRuntime, timeProvider: fakeTime);

        // Act — kick off the check (do not await; reload waits on TimeProvider.Delay)
        var checkTask = sut.CheckAsync();

        // Just before the 5s mark — no reload yet
        fakeTime.Advance(TimeSpan.FromMilliseconds(4990));
        await Task.Yield();
        jsRuntime.InvokedIdentifiers.Should().NotContain("location.reload");

        // Cross the 5s mark — reload fires
        fakeTime.Advance(TimeSpan.FromMilliseconds(20));
        await checkTask;

        // Assert
        jsRuntime.InvokedIdentifiers.Should().ContainSingle(id => id == "location.reload");
    }

    // ---------- helpers ----------

    private static VersionService CreateSut(
        FakeHttpMessageHandler? handler = null,
        IJSRuntime? jsRuntime = null,
        TimeProvider? timeProvider = null,
        ISignalRConnectionEvents? connectionEvents = null,
        ILogger<VersionService>? logger = null)
    {
        handler ??= FakeHttpMessageHandler.RespondingWith(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://test.local/")
        };

        return new VersionService(
            httpClient,
            jsRuntime ?? new RecordingJsRuntime(),
            timeProvider ?? TimeProvider.System,
            logger ?? NullLogger<VersionService>.Instance,
            connectionEvents ?? new StubSignalRConnectionEvents());
    }


    private static HttpResponseMessage BuildHealthResponse(string version) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"status":"healthy","service":"webapi","version":"{{version}}","timestamp":"2026-05-01T00:00:00+00:00"}""",
                Encoding.UTF8,
                "application/json")
        };

    private static string GetClientVersionCore()
    {
        // Use whatever the SUT itself reports — its core is the same as our reference for parity
        var sut = CreateSut();
        var v = sut.ClientVersion;
        var plus = v.IndexOf('+');
        return plus < 0 ? v : v[..plus];
    }

    private static string BumpPatch(string semverCore)
    {
        // Split off any pre-release suffix; we only need to alter a numeric component
        var dashIndex = semverCore.IndexOf('-');
        var numeric = dashIndex < 0 ? semverCore : semverCore[..dashIndex];
        var pre = dashIndex < 0 ? string.Empty : semverCore[dashIndex..];

        var parts = numeric.Split('.');
        if (parts.Length < 3 || !int.TryParse(parts[2], out var patch))
        {
            // Fallback: append a guaranteed-different suffix to keep the test independent
            return semverCore + ".bumped";
        }

        parts[2] = (patch + 1).ToString();
        return string.Join('.', parts) + pre;
    }

    private sealed class StubSignalRConnectionEvents : ISignalRConnectionEvents
    {
        public event Action? Reconnected
        {
            add { _ = value; }
            remove { _ = value; }
        }
    }

    /// <summary>
    /// Records every <see cref="IJSRuntime.InvokeAsync{TValue}(string, object?[])"/> identifier
    /// and returns <c>default(TValue)</c>. Lets tests verify <c>InvokeVoidAsync</c> calls without
    /// depending on the internal <c>IJSVoidResult</c> type that the extension method targets.
    /// </summary>
    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<string> InvokedIdentifiers { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            InvokedIdentifiers.Add(identifier);
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            InvokedIdentifiers.Add(identifier);
            return ValueTask.FromResult<TValue>(default!);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _factory;
        private readonly Exception? _throwException;

        private FakeHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? factory,
            Exception? throwException)
        {
            _factory = factory ?? ((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
            _throwException = throwException;
        }

        public static FakeHttpMessageHandler RespondingWith(HttpStatusCode status, string json) =>
            new((req, ct) => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                RequestMessage = req
            }, throwException: null);

        public static FakeHttpMessageHandler RespondingWithFunc(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> factory) =>
            new(factory, throwException: null);

        public static FakeHttpMessageHandler Throwing(Exception ex) =>
            new(factory: null, throwException: ex);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwException is not null)
            {
                throw _throwException;
            }

            return Task.FromResult(_factory(request, cancellationToken));
        }
    }
}
