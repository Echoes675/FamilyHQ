using System.Text.RegularExpressions;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// FHQ-158. Guards the standing purity rule for <c>tests/</c>: unit tests must not depend on real
/// wall-clock time, on thread scheduling, or on an in-memory database provider.
/// <para>
/// Every one of these has been removed at least once and crept back. The 20-yield settle in
/// SignalRConnectionCoordinatorTests turned master build #59 red on source identical to a green
/// dev, and <c>Task.Delay</c> settles reappeared in TransientHttpRetryHandlerTests one batch after
/// that fix landed — see issue 10 in <c>.agent/docs/intermittent-issues.md</c>. A grep is crude,
/// but it runs in the CI unit-test stage (which loops over <c>tests/*/*.csproj</c>) and it fails
/// on the commit that introduces the regression rather than on a random build weeks later.
/// </para>
/// <para>
/// It lives in FamilyHQ.Core.Tests because that project has no dependencies beyond the test stack;
/// it scans the whole <c>tests/</c> tree regardless of which project it sits in. It reads test
/// SOURCE files only — no product state, no database, no network.
/// </para>
/// <para>
/// Deliberate exceptions go in <see cref="AllowedExceptions"/> with the reason next to them. Adding
/// an entry is a decision to be argued in review, which is the point: the default is "no".
/// </para>
/// </summary>
public class UnitTestPurityGuardTests
{
    private const string RepositoryMarker = "FamilyHQ.slnx";
    private const string TestsFolderName = "tests";

    /// <summary>Constructs that make a unit test depend on real time, real scheduling, or a real-ish database.</summary>
    private static readonly (string Name, string Pattern)[] BannedConstructs =
    [
        // Sleeping to "settle" an async system spends scheduler luck, not a guarantee: the wait can
        // expire before the thing it is waiting for happens. Observe the event instead —
        // TimerArmedTimeProvider for a timer being armed, AwaitableCounter for a callback firing.
        ("Task.Delay", @"Task\.Delay\s*\("),
        ("Thread.Sleep", @"Thread\.Sleep\s*\("),
        // Timing how long something took makes the assertion a function of machine load.
        ("Stopwatch", @"\bStopwatch\b"),
        // The InMemory provider is neither the real database nor a substituted seam: it passes on
        // queries PostgreSQL rejects and vice versa, so it proves nothing either way.
        ("UseInMemoryDatabase", @"UseInMemoryDatabase\s*\(")
    ];

    /// <summary>
    /// (file, construct) pairs that are allowed, each with the reason it is allowed. Keyed on the
    /// path relative to <c>tests/</c>, using forward slashes.
    /// </summary>
    private static readonly Dictionary<(string File, string Construct), string> AllowedExceptions = new()
    {
        // The Simulator's controllers take the concrete SimContext, so there is no repository or
        // data-access interface to substitute. Introducing one means changing production code in
        // tools/FamilyHQ.Simulator, which is out of scope for a test-infra change — tracked as
        // follow-up work. These tests are deterministic today; the debt is correctness-of-approach.
        [("FamilyHQ.Simulator.Tests/Controllers/CalendarsControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-158 follow-up).",
        [("FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-158 follow-up).",
        [("FamilyHQ.Simulator.Tests/Controllers/OAuthControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-158 follow-up).",
        [("FamilyHQ.Simulator.Tests/Controllers/SimulatorConfigControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-158 follow-up).",
        [("FamilyHQ.Simulator.Tests/Controllers/WebhookControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-158 follow-up)."
    };

    [Fact]
    public void UnitTestSources_DoNotUseRealClockSchedulingOrTheInMemoryProvider()
    {
        var testsRoot = Path.Combine(FindRepositoryRoot(), TestsFolderName);
        var violations = new List<string>();

        foreach (var file in EnumerateTestSources(testsRoot))
        {
            var relativePath = Path.GetRelativePath(testsRoot, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            foreach (var (name, pattern) in BannedConstructs)
            {
                if (AllowedExceptions.ContainsKey((relativePath, name)))
                {
                    continue;
                }

                for (var i = 0; i < lines.Length; i++)
                {
                    if (Regex.IsMatch(lines[i], pattern))
                    {
                        violations.Add($"{relativePath}:{i + 1} uses {name} — {lines[i].Trim()}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "unit tests must not depend on real time, thread scheduling or the EF InMemory provider. " +
            "Wait on the event itself (TimerArmedTimeProvider / AwaitableCounter) or substitute the " +
            "data seam. If an exception is genuinely unavoidable, add it to " +
            $"{nameof(UnitTestPurityGuardTests)}.{nameof(AllowedExceptions)} with its justification");
    }

    [Fact]
    public void AllowedExceptions_AreAllStillReachable()
    {
        // A stale allow-list entry silently re-permits the construct in a file that no longer needs
        // it, so the entries are checked against reality rather than trusted.
        var testsRoot = Path.Combine(FindRepositoryRoot(), TestsFolderName);

        var stale = AllowedExceptions.Keys
            .Where(key => !File.Exists(Path.Combine(testsRoot, key.File))
                          || !Regex.IsMatch(
                              File.ReadAllText(Path.Combine(testsRoot, key.File)),
                              BannedConstructs.Single(b => b.Name == key.Construct).Pattern))
            .Select(key => $"{key.File} no longer uses {key.Construct}")
            .ToList();

        stale.Should().BeEmpty("an allow-list entry that no longer applies must be deleted, not left to cover future uses");
    }

    private static IEnumerable<string> EnumerateTestSources(string testsRoot) =>
        Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        // This file names every banned construct in order to look for them.
                        && Path.GetFileName(f) != $"{nameof(UnitTestPurityGuardTests)}.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, RepositoryMarker)))
        {
            directory = directory.Parent;
        }

        // Fail loudly rather than vacuously passing: a guard that silently skips is not a guard.
        directory.Should().NotBeNull(
            $"the repository root (the directory holding {RepositoryMarker}) must be reachable from {AppContext.BaseDirectory}");
        return directory!.FullName;
    }
}
