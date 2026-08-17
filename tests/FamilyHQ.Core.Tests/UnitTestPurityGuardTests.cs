using System.Text;
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
    private const string RepositoryMarker = "FamilyHQ.slnx";
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
        var testsRoot = Path.Combine(FindRepositoryRoot(), TestsFolderName);
        var violations = new List<string>();

        foreach (var file in EnumerateTestSources(testsRoot))
        {
            var relativePath = Path.GetRelativePath(testsRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            var code = MaskCommentsAndLiterals(source);

            foreach (var (name, pattern) in BannedConstructs)
            {
                if (AllowedExceptions.ContainsKey((relativePath, name)))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(code, pattern))
                {
                    var line = LineNumberAt(code, match.Index);
                    violations.Add($"{relativePath}:{line} uses {name} — {LineTextAt(source, line)}");
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
        var code = MaskCommentsAndLiterals(File.ReadAllText(path));
        return Regex.IsMatch(code, BannedConstructs.Single(b => b.Name == construct).Pattern);
    }

    /// <summary>
    /// Blanks C# comments and string/char literals, preserving every character position and line
    /// break so match offsets still map to the original file's line numbers. Without this the guard
    /// fires on its own documentation — a comment reading "never use Task.Delay(" would break the
    /// build, which teaches people to work around the guard rather than to obey it.
    /// </summary>
    private static string MaskCommentsAndLiterals(string source)
    {
        var masked = new StringBuilder(source);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (c == '/' && Next(source, i) == '/')
            {
                var end = source.IndexOf('\n', i);
                i = Blank(masked, source, i, end < 0 ? source.Length : end);
            }
            else if (c == '/' && Next(source, i) == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = Blank(masked, source, i, end < 0 ? source.Length : end + 2);
            }
            else if (c == '"' && Next(source, i) == '"' && Next(source, i + 1) == '"')
            {
                // Raw string literal: N opening quotes, closed by the first run of N quotes.
                var fenceLength = 0;
                while (i + fenceLength < source.Length && source[i + fenceLength] == '"')
                {
                    fenceLength++;
                }

                var close = source.IndexOf(new string('"', fenceLength), i + fenceLength, StringComparison.Ordinal);
                i = Blank(masked, source, i, close < 0 ? source.Length : close + fenceLength);
            }
            else if (c == '@' && Next(source, i) == '"')
            {
                i = Blank(masked, source, i, EndOfVerbatimString(source, i + 2));
            }
            else if (c is '"' or '\'')
            {
                i = Blank(masked, source, i, EndOfSimpleLiteral(source, i + 1, c));
            }
            else
            {
                i++;
            }
        }

        return masked.ToString();
    }

    private static char Next(string source, int index) =>
        index + 1 < source.Length ? source[index + 1] : '\0';

    /// <summary>Index just past the closing quote of a verbatim string, where <c>""</c> escapes a quote.</summary>
    private static int EndOfVerbatimString(string source, int start)
    {
        var i = start;
        while (i < source.Length)
        {
            if (source[i] != '"')
            {
                i++;
            }
            else if (Next(source, i) == '"')
            {
                i += 2;
            }
            else
            {
                return i + 1;
            }
        }

        return source.Length;
    }

    /// <summary>
    /// Index just past the closing quote of a regular string or char literal, where <c>\</c>
    /// escapes the next character. An unterminated literal stops at the end of the line rather
    /// than swallowing the rest of the file.
    /// </summary>
    private static int EndOfSimpleLiteral(string source, int start, char quote)
    {
        var i = start;
        while (i < source.Length && source[i] != quote && source[i] != '\n')
        {
            i += source[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, source.Length);
    }

    /// <summary>Blanks <c>[start, end)</c>, keeping line breaks, and returns <c>end</c>.</summary>
    private static int Blank(StringBuilder target, string source, int start, int end)
    {
        for (var i = start; i < end && i < source.Length; i++)
        {
            if (source[i] is not ('\n' or '\r'))
            {
                target[i] = ' ';
            }
        }

        return Math.Max(end, start + 1);
    }

    private static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string LineTextAt(string source, int lineNumber)
    {
        var lines = source.Split('\n');
        return lineNumber >= 1 && lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : string.Empty;
    }

    private static IEnumerable<string> EnumerateTestSources(string testsRoot) =>
        Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        // This file names every banned construct in order to look for them. The
                        // masking above already covers that, but excluding it keeps the guard's own
                        // correctness from depending on the masker being perfect.
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
