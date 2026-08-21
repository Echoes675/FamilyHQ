namespace FamilyHQ.WebUi.Tests.Services;

/// <summary>
/// Counts occurrences of something the system under test does — a callback invocation, an event
/// being raised — and lets a test await the Nth occurrence instead of polling for it (FHQ-158).
/// <para>
/// The signal is raised from inside the system under test's own continuation, so a waiter resumes
/// the moment the occurrence happens: there is no settle window to get wrong, and no yield or sleep
/// budget that CI load can exhaust (see issue 10 in <c>.agent/docs/intermittent-issues.md</c>). The
/// deadline is a real-clock tripwire on the failure path only — it turns "the occurrence never
/// happened" into a prompt, descriptive failure instead of a hung run.
/// </para>
/// </summary>
internal sealed class AwaitableCounter
{
    private static readonly TimeSpan OccurrenceDeadline = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private TaskCompletionSource _occurred = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _count;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Record()
    {
        lock (_gate)
        {
            _count++;
            _occurred.TrySetResult();
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> occurrences have been recorded.</summary>
    public async Task WaitForAsync(int count)
    {
        while (true)
        {
            Task occurred;
            lock (_gate)
            {
                if (_count >= count)
                {
                    return;
                }

                if (_occurred.Task.IsCompleted)
                {
                    _occurred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                occurred = _occurred.Task;
            }

            try
            {
                await occurred.WaitAsync(OccurrenceDeadline);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"Expected at least {count} occurrence(s) within {OccurrenceDeadline.TotalSeconds:0}s, " +
                    $"but only {Count} were recorded.");
            }
        }
    }
}
