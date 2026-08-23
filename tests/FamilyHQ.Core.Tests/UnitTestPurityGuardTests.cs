using System.Text.RegularExpressions;
using FamilyHQ.Core.Tests.Guards;
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
/// SOURCE files only — no product state, no database, no network. That file read is a deliberate,
/// documented exception to the "no files" rule in <c>.agent/skills/testing-standards/SKILL.md</c>:
/// this is an architecture test over the test sources, and it is not precedent for I/O in a
/// behavioural unit test.
/// </para>
/// <para>
/// <b>What this guard does NOT cover.</b> Read a green run as "these four constructs are absent",
/// not as "the FHQ-158 defect class is impossible":
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>Task.Yield</c>, <c>Task.WaitAsync(timeout)</c> and <c>SemaphoreSlim.WaitAsync(timeout)</c>
///     are deliberately NOT banned — they are the mechanisms this branch keeps. A yield budget
///     backing a NEGATIVE assertion can only ever produce a false pass, never a false CI failure,
///     and the <c>WaitAsync</c> timeouts are failure-path tripwires that turn a hang into a legible
///     5s failure. Banning them would delete the fix along with the defect. A fixed yield budget
///     used to settle a POSITIVE assertion is still the master-#59 bug, and no regex here will
///     tell the two apart — that judgement stays with review.
///   </description></item>
///   <item><description>
///     It is a lexical scan, not the compiler. <c>using static System.Threading.Thread;</c> then a
///     bare <c>Sleep(…)</c>, a <c>using</c> alias, or reaching the same API by reflection all slip
///     through. Receiver and member may be separated by whitespace or a newline and are still
///     caught; renaming the receiver is not. This raises the cost of the regression, it does not
///     make it impossible.
///   </description></item>
///   <item><description>
///     Comments and string/char literals are blanked before scanning, so naming a construct in
///     prose or in an assertion message is safe. The interpolation holes of an interpolated string
///     are treated as literal content, so code hidden in a hole is not scanned either.
///   </description></item>
/// </list>
/// <para>
/// Deliberate exceptions go in <see cref="AllowedExceptions"/> with the reason next to them. Adding
/// an entry is a decision to be argued in review, which is the point: the default is "no".
/// </para>
/// </summary>
public class UnitTestPurityGuardTests
{
    private const string TestsFolderName = "tests";

    /// <summary>
    /// Constructs that make a unit test depend on real time, real scheduling, or a real-ish
    /// database. Patterns tolerate whitespace and newlines between the receiver and the member so
    /// that reformatting cannot walk a construct past the guard.
    /// </summary>
    private static readonly (string Name, string Pattern)[] BannedConstructs =
    [
        // Sleeping to "settle" an async system spends scheduler luck, not a guarantee: the wait can
        // expire before the thing it is waiting for happens. Observe the event instead —
        // TimerArmedTimeProvider for a timer being armed, AwaitableCounter for a callback firing.
        ("Task.Delay", @"\bTask\s*\.\s*Delay\s*\("),
        ("Thread.Sleep", @"\bThread\s*\.\s*Sleep\s*\("),
        // Timing how long something took makes the assertion a function of machine load.
        ("Stopwatch", @"\bStopwatch\b"),
        // The InMemory provider is neither the real database nor a substituted seam: it passes on
        // queries PostgreSQL rejects and vice versa, so it proves nothing either way.
        ("UseInMemoryDatabase", @"\bUseInMemoryDatabase\s*\(")
    ];

    /// <summary>
    /// (file, construct) pairs that are allowed, each with the reason it is allowed. Keyed on the
    /// path relative to <c>tests/</c>, using forward slashes.
    /// </summary>
    private static readonly Dictionary<(string File, string Construct), string> AllowedExceptions = new()
    {
        // The Simulator's controllers take the concrete SimContext, so there is no repository or
        // data-access interface to substitute. Introducing one means changing production code in
        // tools/FamilyHQ.Simulator, which is out of scope for a test-infra change — deferred to
        // FHQ-162. An independent review looked for a fourth route (SQLite in-memory, hand-rolled
        // async DbSet fakes) and found none that avoids a production change or a new package.
        // These tests are deterministic today; the debt is correctness-of-approach.
        [("FamilyHQ.Simulator.Tests/Controllers/CalendarsControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-162).",
        [("FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-162).",
        [("FamilyHQ.Simulator.Tests/Controllers/OAuthControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-162).",
        [("FamilyHQ.Simulator.Tests/Controllers/SimulatorConfigControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-162).",
        [("FamilyHQ.Simulator.Tests/Controllers/WebhookControllerTests.cs", "UseInMemoryDatabase")] =
            "No data seam on SimulatorControllers; substituting one is a production change (FHQ-162)."
    };

    [Fact]
    public void UnitTestSources_DoNotUseRealClockSchedulingOrTheInMemoryProvider()
    {
        var testsRoot = Path.Combine(SourceScan.FindRepositoryRoot(), TestsFolderName);
        var violations = new List<string>();

        foreach (var file in EnumerateTestSources(testsRoot))
        {
            var relativePath = Path.GetRelativePath(testsRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            var code = SourceScan.MaskCommentsAndLiterals(source);

            foreach (var (name, pattern) in BannedConstructs)
            {
                if (AllowedExceptions.ContainsKey((relativePath, name)))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(code, pattern))
                {
                    var line = SourceScan.LineNumberAt(code, match.Index);
                    violations.Add($"{relativePath}:{line} uses {name} — {SourceScan.LineTextAt(source, line)}");
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
        var testsRoot = Path.Combine(SourceScan.FindRepositoryRoot(), TestsFolderName);

        var stale = AllowedExceptions.Keys
            .Where(key => !StillUsesConstruct(Path.Combine(testsRoot, key.File), key.Construct))
            .Select(key => $"{key.File} no longer uses {key.Construct}")
            .ToList();

        stale.Should().BeEmpty("an allow-list entry that no longer applies must be deleted, not left to cover future uses");
    }

    private static bool StillUsesConstruct(string path, string construct)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // Masked exactly as the scan above masks it, so an occurrence that survives only in a
        // comment does not keep a dead entry alive.
        var code = SourceScan.MaskCommentsAndLiterals(File.ReadAllText(path));
        return Regex.IsMatch(code, BannedConstructs.Single(b => b.Name == construct).Pattern);
    }


    private static IEnumerable<string> EnumerateTestSources(string testsRoot) =>
        SourceScan.EnumerateSources(testsRoot, ".cs")
            // This file names every banned construct in order to look for them. SourceScan's masking
            // already covers that, but excluding it keeps the guard's own correctness from depending
            // on the masker being perfect.
            .Where(f => Path.GetFileName(f) != $"{nameof(UnitTestPurityGuardTests)}.cs");
}
