using System.Text.RegularExpressions;
using FamilyHQ.Core.Tests.Guards;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// Guards the standing purity rule for <c>tests/</c>: unit tests must not depend on real
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
/// not as "this defect class is impossible":
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
/// Deliberate exceptions go in <see cref="AllowedExceptions"/> with the permitted count and the
/// reason next to them. Adding an entry is a decision to be argued in review, which is the point: the
/// default is "no". The count is checked for equality, not merely presence, so the entry cannot
/// silently widen into a blanket permit for the whole file: one more occurrence than declared is a
/// new violation hiding behind an existing entry, and one fewer is a stale entry that no longer earns
/// its place.
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
    /// (file, construct) pairs that are allowed, each with how many occurrences are permitted and the
    /// reason. Keyed on the path relative to <c>tests/</c>, using forward slashes.
    /// </summary>
    private static readonly Dictionary<(string File, string Construct), (int Count, string Reason)> AllowedExceptions = new()
    {
        // The controller takes the concrete SimContext, not a repository or data-access interface, so
        // there is no seam to substitute without changing production code in tools/FamilyHQ.Simulator.
        // A review looked for a route around that (SQLite in-memory, hand-rolled async DbSet fakes)
        // and found none that avoids either a production change or a new package. These tests only
        // seed the database and read it back through a GET — no write path runs inside them — so the
        // debt is purity of approach, not weakness of the test.
        [("FamilyHQ.Simulator.Tests/Controllers/CalendarsControllerTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext, so there is no data-access seam to substitute without a production change."),
        // Same constraint as its neighbours, and additionally: these 41 tests write through the
        // controller (create/update/patch/delete/move) and then read the result back, often through a
        // second call that expands recurrence, reminders or all-day boundaries. Splitting a test like
        // that into a verified write plus a separately-seeded read would let each half pass while the
        // pair is wrong, and this file is exactly the shape of this project's worst bug class
        // (recurrence, DST, reminder inheritance). The database-shaped double is the point.
        [("FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext (no seam), and these tests assert net effects over write-then-read sequences that a mock split would weaken."),
        // These tests only seed users and call a read-only endpoint (the auth prompt, consent redirect,
        // token issuance) — no write path runs inside them, so the seam gap is the only reason here.
        [("FamilyHQ.Simulator.Tests/Controllers/OAuthControllerTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext, so there is no data-access seam to substitute without a production change."),
        // Configure replaces a user's calendars and events outright; these tests write through it and
        // then query the database directly to prove the old rows are actually gone and the new ones
        // actually landed — a net effect a mocked write cannot show.
        [("FamilyHQ.Simulator.Tests/Controllers/SimulatorConfigControllerTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext (no seam), and these tests assert net effects over write-then-read sequences that a mock split would weaken."),
        // The backdoor exists so an E2E scenario can change a calendar's Google-side default reminders
        // mid-run; these tests write through it and read the stored JSON back to pin the backdoor's
        // own contract. The real proof that the write matters is the E2E scenario it unblocks, so a
        // bespoke harness beyond the concrete SimContext would cost more than it is worth here.
        [("FamilyHQ.Simulator.Tests/Controllers/BackdoorCalendarsControllerTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext (no seam); these tests write through it and read the stored result back, and the backdoor's real proof is the E2E scenario it unblocks, not a bespoke harness."),
        // Same constraint as its neighbours, and additionally: these tests write through the event
        // endpoints and read the result back. The reminder inheritance rules themselves are covered
        // without any database by ReminderSemanticsTests; only the endpoint wiring — does the
        // controller store and report what the rules say it should — needs this harness.
        [("FamilyHQ.Simulator.Tests/Controllers/EventsControllerRemindersTests.cs", "UseInMemoryDatabase")] =
            (1, "The controller takes the concrete SimContext (no seam), and these tests assert net effects over write-then-read sequences; the reminder rules themselves are covered elsewhere by ReminderSemanticsTests.")
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
                var matches = Regex.Matches(code, pattern);

                if (AllowedExceptions.TryGetValue((relativePath, name), out var allowed))
                {
                    // Equality, deliberately, in both directions: more than declared is a new
                    // violation hiding behind an existing permit, and fewer is a stale permit that no
                    // longer applies.
                    if (matches.Count != allowed.Count)
                    {
                        violations.Add(
                            $"{relativePath} allows {allowed.Count} use(s) of {name} but found {matches.Count} — " +
                            "fix the code if that count is wrong, or update the declared count in " +
                            $"{nameof(AllowedExceptions)} if it is now correct.");
                    }

                    continue;
                }

                foreach (Match match in matches)
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
            $"{nameof(UnitTestPurityGuardTests)}.{nameof(AllowedExceptions)} with its count and justification");
    }

    [Fact]
    public void AllowedExceptions_AreAllStillReachable()
    {
        // The counted check above already fails an entry whose actual count stops matching its
        // declared count, which subsumes "the file still exists but no longer uses the construct" —
        // that would show up there as an actual count of zero. What it cannot see is a declared file
        // that has been deleted outright: the loop above is driven by files that exist on disk, so a
        // dictionary entry naming a file that is gone is never visited and never fails there. This
        // test is kept for that one remaining gap.
        var testsRoot = Path.Combine(SourceScan.FindRepositoryRoot(), TestsFolderName);

        var stale = AllowedExceptions.Keys
            .Where(key => !File.Exists(Path.Combine(testsRoot, key.File)))
            .Select(key => $"{key.File} no longer exists")
            .ToList();

        stale.Should().BeEmpty("an allow-list entry for a file that no longer exists must be deleted, not left to cover a file that might reappear");
    }

    private static IEnumerable<string> EnumerateTestSources(string testsRoot) =>
        SourceScan.EnumerateSources(testsRoot, ".cs")
            // This file names every banned construct in order to look for them. SourceScan's masking
            // already covers that, but excluding it keeps the guard's own correctness from depending
            // on the masker being perfect.
            .Where(f => Path.GetFileName(f) != $"{nameof(UnitTestPurityGuardTests)}.cs");
}
