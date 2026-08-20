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
        // The timeout is the behaviour under test and SemaphoreSlim owns it internally — there is no
        // TimeProvider seam to inject — so this necessarily waits out 50ms of real time. It asserts
        // nothing about how long it took, only that the wait returns instead of throwing, so load
        // can make it slower but can never make it fail (FHQ-158).
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

        // A second wait must now find nothing pending. SemaphoreSlim completes synchronously when a
        // permit is available and only returns an incomplete task when it has to queue the caller,
        // so this reads the outcome directly instead of timing how long the wait blocked for.
        var second = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        second.IsCompleted.Should().BeFalse("only one logical signal was pending, and the first wait consumed it");

        // Drain the queued waiter so the test leaves nothing pending — and prove it was a real
        // waiter that a later release wakes, not a dead task.
        signal.Release();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
