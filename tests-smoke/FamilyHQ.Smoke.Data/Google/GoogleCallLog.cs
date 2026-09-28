using System.Collections.Concurrent;
using System.Globalization;

namespace FamilyHQ.Smoke.Data.Google;

/// <summary>
/// Counts what the suite asks of Google, by operation, for the length of a run.
/// <para>
/// <b>Why this is counted at all.</b> The suite's own traffic was invisible: preprod's calls to Google
/// are in the central log and the suite's were nowhere, so "are we asking Google for too much?" could
/// only be answered by reading code and multiplying. That question stopped being academic when Google
/// began pausing push delivery to this account for minutes at a time after a heavy day — at which point
/// the honest answer to "how many calls did that run make?" was a guess.
/// </para>
/// <para>
/// Counting is not a budget and nothing fails on it. It exists so the number appears in the run output,
/// where it can be compared against the last run and against the day a stall appeared.
/// </para>
/// </summary>
public sealed class GoogleCallLog
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Records one call to <paramref name="operation"/> (<c>list</c>, <c>get</c>, …).</summary>
    public void Record(string operation) =>
        _counts.AddOrUpdate(operation, 1, static (_, existing) => existing + 1);

    /// <summary>Every call counted so far, by operation.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts.ToDictionary(StringComparer.Ordinal);

    /// <summary>The total counted so far.</summary>
    public int Total => _counts.Values.Sum();

    /// <summary>
    /// A point-in-time copy, so a scenario can report what <i>it</i> cost rather than the running total.
    /// </summary>
    public GoogleCallSnapshot Snapshot() => new(Counts);

    /// <summary>
    /// What has been spent since <paramref name="snapshot"/>, rendered for a log line — total first,
    /// then the operations that actually happened, busiest first.
    /// </summary>
    public string Since(GoogleCallSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var delta = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (operation, count) in Counts)
        {
            var spent = count - (snapshot.Counts.TryGetValue(operation, out var before) ? before : 0);
            if (spent > 0)
            {
                delta[operation] = spent;
            }
        }

        return Render(delta);
    }

    /// <summary>The whole run's spend, rendered the same way.</summary>
    public override string ToString() => Render(Counts);

    private static string Render(IReadOnlyDictionary<string, int> counts)
    {
        var total = counts.Values.Sum();
        var parts = counts
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value.ToString(CultureInfo.InvariantCulture)}");

        return $"total={total.ToString(CultureInfo.InvariantCulture)} {string.Join(' ', parts)}".TrimEnd();
    }
}

/// <summary>A copy of the counts at one moment, for measuring a scenario's own spend.</summary>
/// <param name="Counts">The counts as they stood.</param>
public sealed record GoogleCallSnapshot(IReadOnlyDictionary<string, int> Counts);
