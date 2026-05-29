using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class SyncJobSignalTests
{
    [Fact]
    public async Task WaitAsync_ReturnsImmediately_AfterRelease()
    {
        var signal = new SyncJobSignal();
        signal.Release();

        // Should not block: completes well within the timeout.
        await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    [Fact]
    public async Task WaitAsync_TimesOut_WhenNotReleased()
    {
        var signal = new SyncJobSignal();

        // No release: returns when the (short) timeout elapses, without throwing.
        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
    }

    [Fact]
    public async Task Release_Coalesces_MultipleReleasesWakeOnce()
    {
        var signal = new SyncJobSignal();
        signal.Release();
        signal.Release();
        signal.Release();

        await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); // first wait consumes the signal

        // Second wait must now time out (only one logical signal was pending).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(30));
    }
}
