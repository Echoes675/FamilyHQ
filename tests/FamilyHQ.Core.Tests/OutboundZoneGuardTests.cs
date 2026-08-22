using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// FHQ-170. Guards the write-path invariant the fix depends on: <b>a <c>CalendarEvent</c> handed to a
/// Google write must carry its own <c>IanaTimeZone</c></b>, because
/// <c>GoogleCalendarClient.ResolveOutboundZone</c> reads that property and falls back to the FAMILY's
/// configured zone when it is absent. Falling back is right for an event Google never gave a zone
/// (a brand-new one); it is the FHQ-170 defect for one it did — the series is silently re-anchored
/// and every future occurrence moves at the next divergent DST transition, on the phone, for everyone
/// the calendar is shared with.
/// <para>
/// Correctness therefore rests on every construction site remembering an optional property, which is
/// exactly the kind of omission review does not catch: the code compiles, the tests pass, the edit
/// looks right, and the damage appears months later in a different application. Modelled on
/// <see cref="PiiInLogsGuardTests"/> and <see cref="UnitTestPurityGuardTests"/>, which exist for the
/// same class of invisible defect.
/// </para>
///
/// <para><b>What it requires.</b> A <c>new CalendarEvent { … }</c> whose value reaches one of the
/// Google write entry points must ASSIGN <c>IanaTimeZone</c> in its initializer. Assigning
/// <c>null</c> satisfies it: the point is a stated decision at the call site, not a particular value.
/// <c>CalendarEventService.CreateAsync</c> is the case that legitimately writes <c>null</c> — a
/// brand-new event has no zone to preserve.</para>
///
/// <para><b>How it avoids crying wolf.</b> A construction is only required to state a zone when it
/// actually reaches a write:</para>
/// <list type="bullet">
///   <item><description>
///     It is passed by name to <c>CreateEventAsync</c>, <c>CreateRecurringEventAsync</c> or
///     <c>PatchEventFieldsAsync</c> somewhere in the same file, or it is constructed inline inside
///     one of those calls' arguments.
///   </description></item>
///   <item><description>
///     Inbound mapping is untouched: <c>GoogleCalendarClient.GetEventsAsync</c> builds a
///     <c>CalendarEvent</c> per Google item and a tombstone per cancelled one, and neither is ever
///     handed to a write — nothing to state, nothing flagged.
///   </description></item>
///   <item><description>
///     Comments and string literals are blanked first, so prose naming the property cannot satisfy
///     the requirement and a construction quoted in a comment is not scanned.
///   </description></item>
/// </list>
///
/// <para><b>What a green run does NOT mean.</b> Like its siblings this is a lexical tripwire, not a
/// proof. It is known not to catch: a construction in one file handed to a write in another via a
/// helper; a <c>CalendarEvent</c> that reaches a write through a collection, a property or a
/// <c>Select</c>; a loaded entity whose zone is cleared before the write; or a new write entry point
/// that is not added to <see cref="WriteCalls"/>. Loaded rows are deliberately out of scope — they
/// carry whatever the sync stored, which is the value the invariant is about preserving.</para>
/// </summary>
public class OutboundZoneGuardTests
{
    private const string RepositoryMarker = "FamilyHQ.slnx";
    private const string SourceFolderName = "src";

    /// <summary>
    /// The Google write entry points on <c>IGoogleCalendarClient</c>. A <c>CalendarEvent</c> handed to
    /// one of these is serialised into the request body by <c>MapToGoogleEvent</c>, which anchors the
    /// write to the event's own zone when it has one. Matched on a RECEIVER call (<c>x.Method(</c>) so
    /// the interface and client declarations of the same names are not mistaken for call sites.
    /// </summary>
    private static readonly Regex WriteCalls =
        new(@"\.\s*(CreateEventAsync|CreateRecurringEventAsync|PatchEventFieldsAsync)\s*\(");

    /// <summary>An explicitly-typed construction with an object initializer: <c>new CalendarEvent {</c>.</summary>
    private static readonly Regex TypedConstruction = new(@"\bnew\s+CalendarEvent\s*(?:\(\s*\))?\s*\{");

    /// <summary>A target-typed construction: <c>CalendarEvent master = new() {</c>.</summary>
    private static readonly Regex TargetTypedConstruction =
        new(@"\bCalendarEvent\s+(?<name>\w+)\s*=\s*new\s*(?:\(\s*\))?\s*\{");

    /// <summary>The name a construction is assigned to, read backwards from the <c>new</c>.</summary>
    private static readonly Regex AssignedName = new(@"\b(?:var|CalendarEvent)\s+(?<name>\w+)\s*=\s*$");

    /// <summary>The property whose absence hands the write to the family-zone fallback.</summary>
    private static readonly Regex AssignsZone = new(@"\bIanaTimeZone\s*=");

    [Fact]
    public void EveryCalendarEventSentToGoogleStatesTheZoneItIsAnchoredTo()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), SourceFolderName);
        var violations = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);

            violations.AddRange(Scan(source).Select(v =>
                $"{relativePath}:{v.Line} builds {v.Subject} and sends it to Google without setting IanaTimeZone"));
        }

        violations.Should().BeEmpty(
            "an event written to Google must carry the zone Google anchored it to; without it the " +
            "client substitutes the family's configured zone and re-anchors the series (FHQ-170). " +
            "Set IanaTimeZone from the value the event already carries, or assign null where there " +
            "genuinely is no prior zone to preserve (a brand-new event) so the decision is stated " +
            "rather than forgotten");
    }

    [Fact]
    public void TheGuardActuallyReachesTheWritePathsItClaimsToScan()
    {
        // A guard that quietly matches nothing passes forever. Pin both ends: the sources are found,
        // and constructions bound for a Google write are actually being recognised in them.
        var sourceRoot = Path.Combine(FindRepositoryRoot(), SourceFolderName);
        var files = EnumerateProductionSources(sourceRoot).ToList();

        files.Should().HaveCountGreaterThan(100, "src/ holds the whole product");

        var writeBound = files.Sum(f => CountWriteBoundConstructions(File.ReadAllText(f)));

        writeBound.Should().BeGreaterThanOrEqualTo(4,
            "CalendarEventService builds three events for Google writes (create, series master, " +
            "split forward series) and CalendarMigrationService builds a fourth");
    }

    [Fact]
    public void TheGuardFlagsAConstructionHandedToAWriteWithoutAZone()
    {
        const string forgotten = """
            var master = new CalendarEvent { Title = request.Title, Start = start };
            await googleCalendarClient.PatchEventFieldsAsync(owner.GoogleCalendarId, master, hash, ct);
            """;

        ScanSnippet(forgotten).Should().NotBeEmpty("the write will fall back to the family's zone");
    }

    [Fact]
    public void TheGuardAcceptsAnExplicitDecisionIncludingAnExplicitNull()
    {
        const string preserved = """
            var master = new CalendarEvent { Title = t, IanaTimeZone = anchor.TimeZoneId };
            await googleCalendarClient.PatchEventFieldsAsync(owner.GoogleCalendarId, master, hash, ct);
            """;
        const string deliberatelyNone = """
            var created = new CalendarEvent { Title = t, IanaTimeZone = null };
            await googleCalendarClient.CreateEventAsync(cal.GoogleCalendarId, created, hash, ct);
            """;

        ScanSnippet(preserved).Should().BeEmpty("the event carries the zone Google gave it");
        ScanSnippet(deliberatelyNone).Should().BeEmpty(
            "a brand-new event has no prior zone; stating null is the decision, not an omission");
    }

    [Fact]
    public void TheGuardIgnoresConstructionsThatNeverReachAWrite()
    {
        // GoogleCalendarClient.GetEventsAsync maps every Google item into one of these, and a
        // cancelled item into a tombstone. Both are inbound; flagging them would make the guard noise.
        const string inboundMapping = """
            events.Add(new CalendarEvent { GoogleEventId = item.Id, Title = "CANCELLED_TOMBSTONE" });
            """;
        const string localOnly = """
            var projection = new CalendarEvent { Title = row.Title, Start = row.Start };
            return projection;
            """;

        ScanSnippet(inboundMapping).Should().BeEmpty("an inbound tombstone is never written back");
        ScanSnippet(localOnly).Should().BeEmpty("nothing hands this to Google");
    }

    [Fact]
    public void TheGuardReadsAConstructionBuiltInlineInsideTheWriteCall()
    {
        const string inline = """
            await googleCalendarClient.CreateRecurringEventAsync(
                owner.GoogleCalendarId, new CalendarEvent { Title = t, Start = s }, hash, rrule, ct);
            """;

        ScanSnippet(inline).Should().NotBeEmpty("skipping the local is not a way past the invariant");
    }

    [Fact]
    public void TheGuardReadsATargetTypedConstruction()
    {
        const string targetTyped = """
            CalendarEvent newSeries = new() { Title = request.Title, Start = request.Start };
            var created = await googleCalendarClient.CreateRecurringEventAsync(cal.GoogleCalendarId, newSeries, hash, rule, ct);
            """;

        ScanSnippet(targetTyped).Should().NotBeEmpty("`new()` builds the same object as `new CalendarEvent`");
    }

    [Fact]
    public void TheGuardIsNotSatisfiedByProseOrAStringMentioningTheProperty()
    {
        const string commentOnly = """
            // IanaTimeZone is left to the sync to backfill later.
            var master = new CalendarEvent { Title = "IanaTimeZone = evt.IanaTimeZone" };
            await googleCalendarClient.PatchEventFieldsAsync(cal.GoogleCalendarId, master, hash, ct);
            """;

        ScanSnippet(commentOnly).Should().NotBeEmpty("a comment does not set a property");
    }

    /// <summary>A construction flagged by <see cref="Scan"/>, and where.</summary>
    private readonly record struct Violation(int Line, string Subject);

    /// <summary>
    /// The whole pipeline, over one file or one snippet. Both the src/ sweep and the self-tests go
    /// through here, so a self-test cannot pass against a scanner the real run does not use.
    /// </summary>
    private static IReadOnlyList<Violation> Scan(string source)
    {
        var code = MaskCommentsAndLiterals(source);
        var writeArguments = WriteCallArgumentNames(code);
        var writeRegions = WriteCallRegions(code).ToList();
        var violations = new List<Violation>();

        foreach (var (name, brace) in EnumerateConstructions(code))
        {
            var initializer = code[(brace + 1)..MatchingBrace(code, brace)];
            if (AssignsZone.IsMatch(initializer)) continue;

            var byName = name is not null && writeArguments.Contains(name);
            var inline = writeRegions.Any(r => brace > r.Start && brace < r.End);
            if (!byName && !inline) continue;

            violations.Add(new Violation(
                LineNumberAt(source, brace),
                name is null ? "a CalendarEvent" : $"CalendarEvent '{name}'"));
        }

        return violations;
    }

    /// <summary>Runs the same pipeline the guard runs, over one snippet.</summary>
    private static IReadOnlyList<string> ScanSnippet(string snippet) =>
        Scan(snippet).Select(v => v.Subject).ToList();

    private static int CountWriteBoundConstructions(string source)
    {
        var code = MaskCommentsAndLiterals(source);
        var writeArguments = WriteCallArgumentNames(code);
        var writeRegions = WriteCallRegions(code).ToList();

        return EnumerateConstructions(code).Count(c =>
            (c.Name is not null && writeArguments.Contains(c.Name))
            || writeRegions.Any(r => c.Brace > r.Start && c.Brace < r.End));
    }

    /// <summary>
    /// Every <c>CalendarEvent</c> object-initializer in the file, with the local it is assigned to
    /// (null when it is built inline or into a collection) and the index of its opening brace.
    /// </summary>
    private static IEnumerable<(string? Name, int Brace)> EnumerateConstructions(string code)
    {
        foreach (Match match in TypedConstruction.Matches(code))
        {
            // A short window is enough for `var x =` / `CalendarEvent x =` and keeps the anchored
            // backwards match off the whole file.
            var preceding = code[Math.Max(0, match.Index - 120)..match.Index];
            var assignment = AssignedName.Match(preceding);
            yield return (assignment.Success ? assignment.Groups["name"].Value : null, match.Index + match.Length - 1);
        }

        foreach (Match match in TargetTypedConstruction.Matches(code))
        {
            yield return (match.Groups["name"].Value, match.Index + match.Length - 1);
        }
    }

    private readonly record struct Region(int Start, int End);

    private static IEnumerable<Region> WriteCallRegions(string code)
    {
        foreach (Match match in WriteCalls.Matches(code))
        {
            var open = match.Index + match.Length - 1;
            yield return new Region(open, MatchingParenthesis(code, open));
        }
    }

    /// <summary>
    /// The identifiers handed WHOLE to a write call — the top-level arguments that are a single name.
    /// <c>owner.GoogleCalendarId</c> and <c>hash</c> are arguments too, but only a bare local can be
    /// the <c>CalendarEvent</c> a construction produced.
    /// </summary>
    private static HashSet<string> WriteCallArgumentNames(string code)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var region in WriteCallRegions(code))
        {
            foreach (var argument in SplitTopLevel(code[(region.Start + 1)..region.End]))
            {
                var trimmed = argument.Trim();
                if (Regex.IsMatch(trimmed, @"^\w+$")) names.Add(trimmed);
            }
        }

        return names;
    }

    /// <summary>Splits an argument list on commas that are not inside nested brackets.</summary>
    private static IEnumerable<string> SplitTopLevel(string arguments)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return arguments[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return arguments[start..];
    }

    /// <summary>Index of the <c>)</c> closing the <c>(</c> at <paramref name="open"/>, or end of input.</summary>
    private static int MatchingParenthesis(string code, int open) => Matching(code, open, '(', ')');

    /// <summary>Index of the <c>}</c> closing the <c>{</c> at <paramref name="open"/>, or end of input.</summary>
    private static int MatchingBrace(string code, int open) => Matching(code, open, '{', '}');

    private static int Matching(string code, int open, char opening, char closing)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == opening) depth++;
            else if (code[i] == closing && --depth == 0) return i;
        }

        return code.Length;
    }

    /// <summary>
    /// Blanks comments and the contents of every string and char literal, preserving character
    /// positions and line breaks so match offsets still map to the original file's line numbers.
    /// Interpolation holes are blanked with everything else: this guard only reads assignments and
    /// argument names, neither of which lives inside a string.
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
                i = Blank(masked, source, i, EndOfRawString(source, i));
            }
            else if ((c == '@' || c == '$') && StartsQuoted(source, i, out var quote))
            {
                i = Blank(masked, source, i, EndOfVerbatimString(source, quote + 1));
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

    /// <summary>
    /// True when a run of <c>$</c>/<c>@</c> prefixes at <paramref name="index"/> opens a string, with
    /// <paramref name="quote"/> set to the opening quote's index. Both prefixes are treated as
    /// verbatim: over-blanking a non-verbatim interpolated string is harmless here, whereas stopping
    /// early on an escaped quote would leave code masked that is not.
    /// </summary>
    private static bool StartsQuoted(string source, int index, out int quote)
    {
        var i = index;
        while (i < source.Length && source[i] is '$' or '@') i++;

        quote = i;
        return i < source.Length && source[i] == '"';
    }

    private static char Next(string source, int index) =>
        index + 1 < source.Length ? source[index + 1] : '\0';

    /// <summary>Index just past a raw string literal's closing quote run.</summary>
    private static int EndOfRawString(string source, int start)
    {
        var fenceLength = 0;
        while (start + fenceLength < source.Length && source[start + fenceLength] == '"') fenceLength++;

        var close = source.IndexOf(new string('"', fenceLength), start + fenceLength, StringComparison.Ordinal);
        return close < 0 ? source.Length : close + fenceLength;
    }

    /// <summary>Index just past the closing quote of a verbatim string, where <c>""</c> escapes a quote.</summary>
    private static int EndOfVerbatimString(string source, int start)
    {
        var i = start;
        while (i < source.Length)
        {
            if (source[i] != '"') i++;
            else if (Next(source, i) == '"') i += 2;
            else return i + 1;
        }

        return source.Length;
    }

    /// <summary>
    /// Index just past the closing quote of a regular string or char literal, where <c>\</c> escapes
    /// the next character. An unterminated literal stops at the end of the line.
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
            if (source[i] is not ('\n' or '\r')) target[i] = ' ';
        }

        return Math.Max(Math.Min(end, source.Length), start + 1);
    }

    private static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }

        return line;
    }

    private static IEnumerable<string> EnumerateProductionSources(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        // EF-generated migrations and snapshots construct nothing and write nothing.
                        && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

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
