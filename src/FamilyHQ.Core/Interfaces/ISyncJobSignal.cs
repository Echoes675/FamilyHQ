namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// In-process wake signal so an enqueue can wake the worker immediately rather than
/// waiting for the next poll. Single producer-set / single consumer-wait.
/// </summary>
public interface ISyncJobSignal
{
    /// <summary>Wake the worker. Coalesces: multiple releases before a wait wake it once.</summary>
    void Release();

    /// <summary>Wait until signalled or the timeout elapses (the poll backstop).</summary>
    Task WaitAsync(TimeSpan timeout, CancellationToken ct);
}
