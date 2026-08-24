using System.Text.RegularExpressions;
using FamilyHQ.Core.Tests.Guards;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// FHQ-174. Closes the gap the <c>BannedApiAnalyzers</c> rule cannot express: the ban can only name
/// an OVERLOAD, so it waves through every parse that does pass a
/// <see cref="System.Globalization.DateTimeStyles"/> argument — including the wrong one.
/// <para>
/// The rule here is stated as a POSITIVE requirement, deliberately: a
/// <see cref="System.DateTime"/>/<see cref="System.DateTimeOffset"/> parse must say
/// <c>AssumeUniversal</c>. Phrasing it as "must not say <c>AdjustToUniversal</c> alone" was the
/// first attempt and it failed the only test that mattered — an adversarial reviewer set the styles
/// to <c>DateTimeStyles.None</c>, a faithful reintroduction of the original defect, and neither the
/// analyzer nor the guard made a sound. Every wrong answer is a different token; there is only one
/// right one, so the guard looks for the right one.
/// </para>
/// <para>
/// <c>AssumeUniversal</c> is what supplies the zone a date-only string does not carry.
/// <c>AdjustToUniversal</c> only normalises a value that already has one; on a Google all-day
/// <c>date</c> or an RRULE <c>UNTIL</c> in date form it does nothing at all, and the parse falls back
/// to the HOST machine's offset (for <see cref="System.DateTimeOffset"/>) or leaves
/// <see cref="System.DateTimeKind.Unspecified"/> for an EF converter to reinterpret as host-local
/// (for <see cref="System.DateTime"/>).
/// </para>
/// <para>
/// It has to be a source scan rather than an assertion about values, for the same reason the ban
/// does: CI runs at a zero host offset, so the wrong styles and the right styles produce identical
/// results there. A value-based test would pass on the defect.
/// </para>
///
/// <para><b>What a green run does NOT mean.</b> This is a lexical tripwire, like
/// <see cref="PiiInLogsGuardTests"/>, <see cref="OutboundZoneGuardTests"/> and
/// <see cref="UnitTestPurityGuardTests"/>, and it shares their masker (<see cref="SourceScan"/>), so
/// it shares their limits. It is known not to catch: a parse reached through a wrapper, an alias or
/// reflection; and, in a <c>.razor</c> file, C# written inside a markup ATTRIBUTE
/// (<c>@onclick="…"</c>), which the C# masker cannot tell from a string literal. A component's
/// <c>@code</c> block is scanned normally.</para>
/// </summary>
public class DateOnlyParseGuardTests
{
    /// <summary>The roots scanned: product code and the Google test double, which must agree.</summary>
    private static readonly string[] ScannedFolders = ["src", "tools"];

    /// <summary>
    /// <c>.razor</c> is scanned as well as <c>.cs</c>. The kiosk's event editor builds all-day
    /// boundaries in a component's <c>@code</c> block, so leaving Razor out would have left the very
    /// files FHQ-174 changed unguarded.
    /// </summary>
    private static readonly string[] ScannedExtensions = [".cs", ".razor"];

    private const string AdjustToUniversal = "AdjustToUniversal";
    private const string AssumeUniversal = "AssumeUniversal";

    /// <summary>
    /// <c>AssumeUniversal</c> as a whole identifier, so a look-alike name that merely begins with it
    /// cannot satisfy the presence rule.
    /// </summary>
    private static readonly Regex NamesAssumeUniversal =
        new($@"\b{AssumeUniversal}\b", RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>DateTime.Parse(</c>, <c>DateTimeOffset.TryParseExact(</c> and the rest. Only these
    /// two receivers: <c>DateOnly</c> and <c>TimeOnly</c> model a date or a time with no zone at all,
    /// so <c>AssumeUniversal</c> is meaningless for them (and rejected outright by
    /// <c>TimeOnly.TryParseExact</c>).
    /// </summary>
    private static readonly Regex ZonedParseCall =
        new(@"\b(?:DateTime|DateTimeOffset)\s*\.\s*(?:TryParseExact|ParseExact|TryParse|Parse)\s*\(");

    [Fact]
    public void EveryDateOrDateTimeParseSaysWhatZoneAZonelessValueIsIn()
    {
        var repositoryRoot = SourceScan.FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var folder in ScannedFolders)
        {
            foreach (var file in EnumerateSources(Path.Combine(repositoryRoot, folder)))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                violations.AddRange(Scan(File.ReadAllText(file)).Select(v => $"{relativePath}:{v}"));
            }
        }

        violations.Should().BeEmpty(
            "a parse that does not state a zone-less value's zone falls back to the host machine's " +
            "offset (FHQ-174). Pass DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal " +
            "at the call site, or use FamilyHQ.Core.Calendar.GoogleAllDayDate for a Google all-day " +
            "date. Found:\n" + string.Join("\n", violations));
    }

    // ── The guard's own coverage ──────────────────────────────────────────────
    //
    // A guard nobody has watched fail is a guard nobody knows works. These run the real scan over
    // snippets: the first three are the reintroductions that got past the previous version, the rest
    // are the shapes it must NOT fire on.

    [Fact]
    public void AStylesArgumentThatOmitsAssumeUniversalIsFlagged() =>
        // The reviewer's reintroduction, in the form it takes at a call site.
        ScanSnippet("""
            var ok = DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
            """)
            .Should().ContainSingle().Which.Should().Contain("does not pass DateTimeStyles.AssumeUniversal");

    [Fact]
    public void AStylesArgumentHoistedIntoANamedConstantIsFlagged() =>
        // The reviewer's reintroduction in the form it ACTUALLY took: the flags moved into a const,
        // where the wrong value is invisible at the call site. The guard cannot resolve the constant,
        // so it insists the flags be written where it — and a reviewer — can read them.
        ScanSnippet("""
            var ok = DateTimeOffset.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateOnlyStyles, out var parsed);
            """)
            .Should().ContainSingle();

    [Fact]
    public void AnOmittedStylesArgumentIsFlagged() =>
        // Also banned by the analyzer, but only inside the four projects that import the ban; here it
        // is caught everywhere the guard scans.
        ScanSnippet("var start = DateTimeOffset.Parse(item.Start.Date, CultureInfo.InvariantCulture);")
            .Should().ContainSingle();

    [Fact]
    public void AdjustToUniversalWithoutAssumeUniversalIsFlaggedEvenAwayFromACallSite() =>
        // The flags can be defined nowhere near a parse. Catching the definition is the only way to
        // catch that.
        ScanSnippet("private const DateTimeStyles Styles = DateTimeStyles.AdjustToUniversal;")
            .Should().ContainSingle().Which.Should().Contain("without DateTimeStyles.AssumeUniversal");

    [Fact]
    public void TheSanctionedFormIsNotFlagged() =>
        ScanSnippet("""
            var ok = DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed);
            """)
            .Should().BeEmpty();

    [Fact]
    public void AStringLiteralNamingTheFlagIsNotFlagged() =>
        // The false positive the previous version had: it blanked whole-line comments only, so a
        // const string mentioning the flag failed the build. A guard that fires on prose about itself
        // teaches reviewers to add suppressions, which is the opposite of what it is for.
        ScanSnippet("""
            private const string Hint = "pass DateTimeStyles.AdjustToUniversal with AssumeUniversal";
            private const string Terser = "DateTimeStyles.AdjustToUniversal";
            """)
            .Should().BeEmpty();

    [Fact]
    public void ATrailingCommentNamingTheFlagIsNotFlagged() =>
        ScanSnippet("var x = 1; // never DateTimeStyles.AdjustToUniversal on its own")
            .Should().BeEmpty();

    [Fact]
    public void AZonelessDateOnlyOrTimeOnlyParseIsNotFlagged() =>
        // DateOnly and TimeOnly carry no zone by construction; AssumeUniversal is meaningless there
        // and TimeOnly.TryParseExact rejects it.
        ScanSnippet("""
            var d = DateOnly.Parse(block.Time[i], CultureInfo.InvariantCulture);
            var ok = TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
            """)
            .Should().BeEmpty();

    [Fact]
    public void RazorFilesAreScanned()
    {
        // The blind spot the previous version had: it globbed *.cs only, so the kiosk component that
        // builds all-day boundaries was never read.
        var sourceRoot = Path.Combine(SourceScan.FindRepositoryRoot(), "src");

        EnumerateSources(sourceRoot)
            .Should().Contain(f => f.EndsWith(".razor", StringComparison.Ordinal),
                "a Blazor component's @code block is product C# and has carried this defect class");
    }

    [Fact]
    public void AViolationInsideARazorCodeBlockIsFlagged() =>
        ScanSnippet("""
            <div class="mb-3">@_label</div>
            @code {
                private void Seed() => _start = DateTimeOffset.Parse(_raw, CultureInfo.InvariantCulture);
            }
            """)
            .Should().ContainSingle();

    // ── The scan ──────────────────────────────────────────────────────────────

    /// <summary>Runs the real scan over one snippet, returning the messages it produced.</summary>
    private static IReadOnlyList<string> ScanSnippet(string snippet) => Scan(snippet);

    private static IReadOnlyList<string> Scan(string source)
    {
        var code = SourceScan.MaskCommentsAndLiterals(source);
        var violations = new List<string>();

        // Rule 1 (presence). Every DateTime/DateTimeOffset parse must name AssumeUniversal in its own
        // argument list. Stated positively so that DateTimeStyles.None, a hoisted constant and an
        // omitted styles argument all fail — the previous "absence of AdjustToUniversal" phrasing
        // passed on all three.
        foreach (Match match in ZonedParseCall.Matches(code))
        {
            var open = match.Index + match.Length - 1;
            var arguments = code[(open + 1)..SourceScan.MatchingParenthesis(code, open)];

            // Whole-identifier match, not a substring: a plain Contains is satisfied by any name
            // that merely STARTS with the flag — `AssumeUniversalOff` would sail through the rule
            // written to stop exactly this kind of indirection.
            if (NamesAssumeUniversal.IsMatch(arguments))
                continue;

            violations.Add(
                $"{SourceScan.LineNumberAt(code, match.Index)} parses with " +
                $"{match.Value.TrimEnd('(').Replace(" ", string.Empty)} but does not pass " +
                $"DateTimeStyles.{AssumeUniversal}");
        }

        // Rule 2 (composition). The flags can be built somewhere other than a call site — a constant,
        // a local, a field. AdjustToUniversal on its own is always wrong wherever it is written.
        for (var at = code.IndexOf(AdjustToUniversal, StringComparison.Ordinal);
             at >= 0;
             at = code.IndexOf(AdjustToUniversal, at + 1, StringComparison.Ordinal))
        {
            if (EnclosingStatement(code, at).Contains(AssumeUniversal, StringComparison.Ordinal))
                continue;

            violations.Add(
                $"{SourceScan.LineNumberAt(code, at)} uses DateTimeStyles.{AdjustToUniversal} " +
                $"without DateTimeStyles.{AssumeUniversal}");
        }

        return violations;
    }

    /// <summary>
    /// The text of the statement containing <paramref name="index"/> — from the previous statement
    /// or block boundary to the next one. Both style flags of a single parse call always sit inside
    /// one such span.
    /// </summary>
    private static string EnclosingStatement(string code, int index)
    {
        var start = code.LastIndexOfAny([';', '{', '}'], index) + 1;
        var end = code.IndexOfAny([';', '{', '}'], index);
        return code[start..(end < 0 ? code.Length : end)];
    }

    private static IEnumerable<string> EnumerateSources(string root) =>
        SourceScan.EnumerateSources(root, ScannedExtensions)
            // EF-generated migrations parse nothing.
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));
}
