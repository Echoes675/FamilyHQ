using FamilyHQ.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

public class AccessTokenCacheTests
{
    private static Func<CancellationToken, Task<(string, int)>> Counting(Action? onCall = null, string token = "tok", int expiresIn = 3600)
        => _ => { onCall?.Invoke(); return Task.FromResult((token, expiresIn)); };

    [Fact]
    public async Task Miss_InvokesRefreshOnce_ReturnsToken()
    {
        var calls = 0;
        var sut = new AccessTokenCache(new FakeTimeProvider());
        var result = await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        result.Should().Be("tok");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task SecondCallWithinWindow_DoesNotRefreshAgain()
    {
        var calls = 0;
        var sut = new AccessTokenCache(new FakeTimeProvider());
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        calls.Should().Be(1);
    }

    [Fact]
    public async Task AfterExpiry_RefreshesAgain()
    {
        var calls = 0;
        var time = new FakeTimeProvider();
        var sut = new AccessTokenCache(time);
        await sut.GetOrRefreshAsync("u", Counting(() => calls++, expiresIn: 3600));
        time.Advance(TimeSpan.FromSeconds(3600));   // past expiresAt (3600 - 60 skew)
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        calls.Should().Be(2);
    }

    [Fact]
    public async Task NearExpiryWithinSkew_TreatedAsExpired()
    {
        var calls = 0;
        var time = new FakeTimeProvider();
        var sut = new AccessTokenCache(time);
        await sut.GetOrRefreshAsync("u", Counting(() => calls++, expiresIn: 3600));
        time.Advance(TimeSpan.FromSeconds(3541));    // within the 60s skew of raw 3600 → expired
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Evict_ForcesRefresh()
    {
        var calls = 0;
        var sut = new AccessTokenCache(new FakeTimeProvider());
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        sut.Evict("u");
        await sut.GetOrRefreshAsync("u", Counting(() => calls++));
        calls.Should().Be(2);
    }

    [Fact]
    public async Task RefreshThrows_NotCached_ThenRetries()
    {
        var calls = 0;
        var sut = new AccessTokenCache(new FakeTimeProvider());
        Func<CancellationToken, Task<(string, int)>> throwing = _ => { calls++; throw new InvalidOperationException("boom"); };

        await sut.Invoking(s => s.GetOrRefreshAsync("u", throwing)).Should().ThrowAsync<InvalidOperationException>();
        await sut.Invoking(s => s.GetOrRefreshAsync("u", throwing)).Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(2);   // nothing cached → both attempts call refresh
    }

    [Fact]
    public async Task ConcurrentColdCalls_RefreshInvokedExactlyOnce()
    {
        var time = new FakeTimeProvider();
        var sut = new AccessTokenCache(time);
        var calls = 0;
        var hold = new TaskCompletionSource();
        Func<CancellationToken, Task<(string, int)>> refresh = async _ =>
        {
            Interlocked.Increment(ref calls);
            await hold.Task;                 // hold the first refresh so the rest queue on the gate
            return ("tok", 3600);
        };

        var tasks = Enumerable.Range(0, 10).Select(_ => sut.GetOrRefreshAsync("u", refresh)).ToArray();
        hold.SetResult();
        var results = await Task.WhenAll(tasks);

        calls.Should().Be(1);
        results.Should().OnlyContain(t => t == "tok");
    }
}
