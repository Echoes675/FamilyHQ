using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// SemaphoreSlim-backed wake signal. Release() coalesces (never accumulates beyond one
/// pending permit) so a burst of enqueues wakes the single worker exactly once.
/// </summary>
public sealed class SyncJobSignal : ISyncJobSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Release()
    {
        // CurrentCount==1 means a permit is already pending; Release would throw
        // SemaphoreFullException. Guard so coalescing is safe under concurrent producers.
        if (_semaphore.CurrentCount == 0)
        {
            try { _semaphore.Release(); }
            catch (SemaphoreFullException) { /* raced to 1 — already signalled */ }
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
        => await _semaphore.WaitAsync(timeout, ct);
}
