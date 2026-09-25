namespace FamilyHQ.Smoke.Common.Helpers;

/// <summary>
/// One bounded wait for an asynchronous outcome, and the reason the smoke suite has nothing else.
/// <para>
/// FHQ-141 principle 2 forbids <i>compensation</i>: no retrying a failed check, no triggering a sync
/// by hand, no extending a deadline because the first attempt missed. This is not that. Google push
/// travels Google → RelayRobin → preprod → sync queue → API, and asking "has it arrived?" is the
/// assertion itself — the deadline is the specification of how long the live path is allowed to take.
/// When it expires the scenario fails, and it fails with <paramref name="failureDescription"/>
/// naming the path that did not deliver, because "condition was false" is not actionable at 3am.
/// </para>
/// <para>
/// There is deliberately no overload that retries, swallows or re-runs anything. A caller that wants
/// a second chance has to write it, in the open, where review can see it.
/// </para>
/// </summary>
public static class BoundedWait
{
    private const int DefaultPollIntervalMs = 2000;

    /// <summary>
    /// Polls <paramref name="condition"/> until it is true or <paramref name="timeout"/> elapses,
    /// then throws a <see cref="TimeoutException"/> carrying <paramref name="failureDescription"/>.
    /// </summary>
    public static async Task UntilAsync(
        Func<Task<bool>> condition,
        string failureDescription,
        TimeSpan timeout,
        int pollIntervalMs = DefaultPollIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(pollIntervalMs);
        }

        throw new TimeoutException(
            $"{failureDescription} (waited {timeout.TotalSeconds:0}s).");
    }

    /// <summary>
    /// Polls <paramref name="produce"/> until it returns a non-null value or the timeout elapses.
    /// Saves callers a nullable capture plus a second read of the same thing they just waited for.
    /// </summary>
    public static async Task<T> ForAsync<T>(
        Func<Task<T?>> produce,
        string failureDescription,
        TimeSpan timeout,
        int pollIntervalMs = DefaultPollIntervalMs)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(produce);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var value = await produce();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(pollIntervalMs);
        }

        throw new TimeoutException(
            $"{failureDescription} (waited {timeout.TotalSeconds:0}s).");
    }
}
