using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// FHQ-166. Guards the "Redaction (non-negotiable)" rule in <c>.agent/skills/logging/SKILL.md</c>
/// for the values that had already leaked past it: Google calendar ids (a primary calendar's id
/// <b>is</b> the account's email address), calendar display names (the Google <c>summary</c> —
/// again an email address for a primary calendar, and a child's name for a member calendar), and
/// the family's home place name and coordinates.
/// <para>
/// Modelled on <see cref="UnitTestPurityGuardTests"/> and for the same reason: nothing about the
/// type <c>string CalendarId</c> says "email address", so this defect class is invisible on review
/// and re-enters the moment someone adds a helpful log line. A grep is crude, but it runs in the CI
/// unit-test stage and fails on the commit that reintroduces the leak.
/// </para>
///
/// <para><b>How it avoids crying wolf.</b> It does not flag every mention of a sensitive property —
/// only values <i>handed to a log call, a logging scope or an exception constructor</i>:</para>
/// <list type="bullet">
///   <item><description>
///     Comments and the literal text of every string are blanked first, so a message template
///     reading "Duplicate calendar display name during sync" and prose naming a property are both
///     invisible to the scan. The <i>interpolation holes</i> of a <c>$"…"</c> string are preserved,
///     because that is the one place an exception message can carry a value.
///   </description></item>
///   <item><description>
///     Only a <b>terminal</b> member access counts. <c>calendarByName[localCal.DisplayName].Id</c>
///     logs the id, not the name, and is not flagged; <c>currentOwner.DisplayName</c> is.
///   </description></item>
///   <item><description>
///     The match must sit in <b>argument position</b> — the value is handed to the call, not merely
///     read while computing an argument. <c>cals.Count(c => c.DisplayName != null)</c> passes a
///     count; <c>nameof(displayName)</c> passes the parameter's name. Neither discloses anything,
///     and both are ordinary enough that flagging them would get the guard deleted rather than
///     obeyed. The arguments of <c>nameof(…)</c> are blanked outright for the same reason: a
///     <c>throw new ArgumentNullException(nameof(placeName))</c> guard clause is the standard .NET
///     form and must not fail the build.
///   </description></item>
///   <item><description>
///     The arguments of a <c>Redact(…)</c> call are blanked. Passing a sensitive value through
///     <c>IPiiRedactor</c> is the sanctioned way to log it, and it is the only one — there is no
///     allow-list here on purpose, so "make it stop failing" means "route it through the redactor
///     or log an id we own", never "add a line to the exceptions dictionary".
///   </description></item>
/// </list>
///
/// <para><b>What a green run does NOT mean.</b> This is a lexical scan, not the compiler, and it is
/// a tripwire rather than a proof. It is known NOT to catch:</para>
/// <list type="bullet">
///   <item><description>
///     <b>Aliasing.</b> <c>var owner = calendar.DisplayName;</c> followed by
///     <c>logger.LogInformation("{Owner}", owner)</c> — the assignment is not a sink call and
///     <c>owner</c> is not a banned name.
///   </description></item>
///   <item><description>
///     <b>Plural and derived locals.</b> The bare-local patterns are the singular forms actually
///     used at these call sites; <c>memberCalendarNames</c> (which really does hold display names)
///     matches none of them.
///   </description></item>
///   <item><description>
///     <b>Values reached through an expression.</b> Only the terminal token of a top-level argument
///     is tested, so <c>string.Join(", ", cals.Select(c => c.DisplayName))</c> passes. This is the
///     price of not flagging the safe forms above, and it is the right trade: a guard that fails the
///     build on idiomatic code does not survive.
///   </description></item>
///   <item><description>
///     <b>Indirect sinks.</b> <c>[LoggerMessage]</c> source-generated logging, a helper method that
///     logs on its caller's behalf, reflection, and serialising a whole entity into a structured
///     property. The one destructuring form the skill names explicitly — <c>{@Object}</c> — IS
///     caught, by scanning the unmasked message template of each log call.
///   </description></item>
///   <item><description>
///     <b>New PII.</b> The banned set is the properties this codebase actually has. A new
///     PII-bearing property is not covered until it is added to <see cref="BannedValues"/>.
///   </description></item>
///   <item><description>
///     <b>Anything outside <c>src/</c>.</b> <c>tests/</c> and <c>tests-e2e/</c> use fixture values,
///     and <c>tools/FamilyHQ.Simulator</c> holds only data it generates itself — the reasoning for
///     that exemption lives in <c>.agent/skills/logging/SKILL.md</c>, where a reader looking for the
///     rule will find it.
///   </description></item>
/// </list>
/// </summary>
public class PiiInLogsGuardTests
{
    private const string RepositoryMarker = "FamilyHQ.slnx";
    private const string SourceFolderName = "src";

    private const string LogCallKind = "log call";
    private const string LogScopeKind = "logging scope";
    private const string ExceptionKind = "exception message";

    /// <summary>
    /// Values that must never reach a log sink or an exception message verbatim, and the reason.
    /// <para>
    /// The member-access patterns end in a negative lookahead that keeps only the TERMINAL access:
    /// not followed by another identifier character, not followed by <c>.</c> (a further member
    /// access), and not followed by <c>]</c> then <c>.</c> (an indexer key, where the logged value
    /// is whatever the indexer returns).
    /// </para>
    /// <para>
    /// Each member-access pattern is paired with a bare-local pattern, because the value is just as
    /// disclosing once it has been passed into a method as a parameter. FHQ-166's review found the
    /// gap: <c>latitude</c>/<c>longitude</c> had only the member-access form, and
    /// <c>SunCalculatorService</c> put both into four exception messages as plain parameters.
    /// </para>
    /// </summary>
    private static readonly (string Value, string Reason, string Pattern)[] BannedValues =
    [
        ("GoogleCalendarId",
         "a Google PRIMARY calendar's id IS the account's email address",
         @"\.\s*GoogleCalendarId(?![\w.]|\s*\]\s*\.)"),

        ("DisplayName",
         "a calendar's display name is its Google `summary` — the account's email address for a " +
         "primary calendar, a family member's name for a member calendar",
         @"\.\s*DisplayName(?![\w.]|\s*\]\s*\.)"),

        ("PlaceName",
         "the configured place name is the family's home address; LocationId correlates just as well",
         @"\.\s*PlaceName(?![\w.]|\s*\]\s*\.)"),

        ("Latitude",
         "the stored latitude pinpoints the family home to within a few metres",
         @"\.\s*Latitude(?![\w.]|\s*\]\s*\.)"),

        ("Longitude",
         "the stored longitude pinpoints the family home to within a few metres",
         @"\.\s*Longitude(?![\w.]|\s*\]\s*\.)"),

        ("Email",
         "an email address is PII under the logging standard, in a message or a structured property",
         @"\.\s*(Organizer)?Email(?![\w.]|\s*\]\s*\.)"),

        ("googleCalendarId",
         "a Google calendar id held in a local or parameter is still an email address",
         @"\b(google|source|destination)CalendarId\b"),

        ("placeName",
         "a place name held in a local or parameter is still the family's home address",
         @"\bplaceName\b"),

        ("displayName",
         "a calendar display name held in a local or parameter is still PII",
         @"\b(display|calendar|member)Name\b"),

        ("latitude",
         "a latitude held in a local or parameter still pinpoints the family home",
         @"\blatitude\b"),

        ("longitude",
         "a longitude held in a local or parameter still pinpoints the family home",
         @"\blongitude\b")
    ];

    /// <summary>
    /// Call sites whose arguments carry a value all the way to Seq.
    /// <list type="bullet">
    ///   <item><description>
    ///     Every <c>ILogger</c> entry point: the <c>LogTrace…LogCritical</c> extensions and the
    ///     <c>Log(level, …)</c> method they all delegate to.
    ///   </description></item>
    ///   <item><description>
    ///     <c>BeginScope(…)</c>, because a scope property is attached to <i>every</i> record emitted
    ///     inside the scope — a wider blast radius than a single log line, not a smaller one.
    ///   </description></item>
    ///   <item><description>
    ///     Exception constructors, because an address in an exception message reaches the same sink
    ///     as one in a log template — via the handler that logs it — and can reach the client
    ///     through ProblemDetails. The type name may be namespace-qualified.
    ///   </description></item>
    /// </list>
    /// </summary>
    private static readonly (string Kind, Regex Start)[] SinkCalls =
    [
        (LogCallKind, new Regex(@"\.\s*Log(Trace|Debug|Information|Warning|Error|Critical)?\s*\(")),
        (LogScopeKind, new Regex(@"\.\s*BeginScope\s*\(")),
        (ExceptionKind, new Regex(@"\bnew\s+(?:[A-Za-z_]\w*\s*\.\s*)*[A-Za-z_]\w*Exception\s*\("))
    ];

    /// <summary>The sanctioned escape hatch: <see cref="Core.Interfaces.IPiiRedactor.Redact"/>.</summary>
    private static readonly Regex RedactCall = new(@"\bRedact\s*\(");

    /// <summary>
    /// <c>nameof(x)</c> yields the identifier <c>"x"</c>, never its value, so its argument is not a
    /// disclosure — and <c>throw new ArgumentNullException(nameof(placeName))</c> is the standard
    /// .NET guard clause.
    /// </summary>
    private static readonly Regex NameofCall = new(@"\bnameof\s*\(");

    /// <summary>
    /// Serilog/MEL destructuring, which the logging skill bans outright: <c>{@Calendar}</c> writes
    /// every property of the object — <c>DisplayName</c> and <c>GoogleCalendarId</c> included — into
    /// the log event. It is matched against the RAW message template, because the masking pass that
    /// makes everything else tractable blanks the very text this hazard lives in.
    /// </summary>
    private static readonly Regex DestructuringHole = new(@"\{@\w");

    private const string DestructuringReason =
        "`{@Object}` destructures every property of the argument into the log event, including any " +
        "the object happens to carry — log named, individually-chosen fields instead";

    [Fact]
    public void ProductionSources_DoNotLogEmailAddressesCalendarIdsDisplayNamesOrTheHomeLocation()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), SourceFolderName);
        var violations = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);

            violations.AddRange(Scan(source).Select(v =>
                $"{relativePath}:{v.Line} passes {v.Value} to a {v.Kind} — {v.Reason}. " +
                $"Line: {LineTextAt(source, v.Line)}"));
        }

        violations.Should().BeEmpty(
            "these values must never reach Seq verbatim (.agent/skills/logging/SKILL.md, " +
            "\"Redaction (non-negotiable)\"). Log an identifier FamilyHQ owns instead — a " +
            "CalendarInfo.Id, a LocationId, a user id — or, where the caller genuinely holds only " +
            "the third-party value, pass it through IPiiRedactor.Redact so the log line keeps a " +
            "stable, non-reversible token. There is deliberately no allow-list");
    }

    [Fact]
    public void TheGuardActuallyReachesTheProductionSourcesItClaimsToScan()
    {
        // A guard that quietly scans nothing passes forever. Pin both ends: the files are found,
        // and log calls are actually being recognised inside them.
        var sourceRoot = Path.Combine(FindRepositoryRoot(), SourceFolderName);
        var files = EnumerateProductionSources(sourceRoot).ToList();

        files.Should().HaveCountGreaterThan(100, "src/ holds the whole product");

        var logCalls = files
            .Select(f => Mask(File.ReadAllText(f)))
            .Sum(code => EnumerateSinkCallArguments(code).Count(r => r.Kind == LogCallKind));

        logCalls.Should().BeGreaterThan(100,
            "the log-call matcher must still recognise this codebase's logging style");
    }

    [Fact]
    public void TheGuardDistinguishesALoggedNameFromANameUsedOnlyAsALookupKey()
    {
        // The one false positive worth pinning: CalendarSyncService's duplicate-name warning reads
        // calendarByName[localCal.DisplayName].Id — it logs the ID, not the name. A guard that
        // flagged it would be edited out of existence within a week.
        const string lookupKeyOnly = """
            logger.LogWarning("Duplicate calendar display name", localCal.Id, calendarByName[localCal.DisplayName].Id);
            """;
        const string nameLogged = """
            logger.LogInformation("Syncing {CalendarName}.", calendar.DisplayName);
            """;

        ScanSnippet(lookupKeyOnly).Should().BeEmpty("a dictionary key is not the logged value");
        ScanSnippet(nameLogged).Should().NotBeEmpty("the display name itself is the logged value");
    }

    [Fact]
    public void TheGuardIgnoresProseAndTemplatesButReadsInterpolationHoles()
    {
        const string proseOnly = """
            logger.LogWarning("Duplicate display name for calendar {CalendarInfoId} at PlaceName scope.", cal.Id);
            """;
        const string interpolatedException = """
            throw new InvalidOperationException($"Calendar {cal.GoogleCalendarId} has no sync window.");
            """;
        const string redacted = """
            logger.LogWarning("Cap reached for calendar {CalendarIdToken}.", _piiRedactor.Redact(googleCalendarId));
            """;

        ScanSnippet(proseOnly).Should().BeEmpty("a message template naming a property discloses nothing");
        ScanSnippet(interpolatedException).Should().NotBeEmpty("an interpolation hole carries the real value");
        ScanSnippet(redacted).Should().BeEmpty("routing through IPiiRedactor is the sanctioned escape hatch");
    }

    [Fact]
    public void TheGuardReadsEveryHoleOfAMultiValueMessageNotJustTheLastOne()
    {
        // FHQ-166 review: SunCalculatorService's four polar-location messages read
        // "lat={latitude}, lon={longitude}, date={date}" — the family's home coordinates, arriving
        // as plain parameters from DayThemeService and logged at Error by DayThemeSchedulerService.
        // Nothing flagged them, because the guard only knew the `.Latitude` member-access shape.
        const string coordinatesInAnExceptionMessage = """
            throw new InvalidOperationException($"Sun phase 'Dawn' is not available for lat={latitude}, lon={longitude}, date={date}.");
            """;

        ScanSnippet(coordinatesInAnExceptionMessage)
            .Should().Contain("latitude").And.Contain("longitude");
    }

    [Fact]
    public void TheGuardDoesNotFailTheBuildOnSafeIdiomaticCode()
    {
        // Every one of these is PII-free, and the first two are the standard .NET guard clause —
        // IGeocodingService.GeocodeAsync really does take a `string placeName`. A guard that failed
        // on them would leave the next developer no move except deleting the guard, since there is
        // deliberately no allow-list to add an entry to.
        const string nullGuardClause = """
            throw new ArgumentNullException(nameof(placeName));
            """;
        const string blankGuardClause = """
            throw new ArgumentException("must not be blank", nameof(displayName));
            """;
        const string countingWithoutLogging = """
            logger.LogInformation("Kept {Count}", cals.Count(c => c.DisplayName != null));
            """;

        ScanSnippet(nullGuardClause).Should().BeEmpty("nameof yields the parameter's name, never its value");
        ScanSnippet(blankGuardClause).Should().BeEmpty("nameof yields the parameter's name, never its value");
        ScanSnippet(countingWithoutLogging).Should().BeEmpty(
            "the argument is a count; the display name is only a predicate input");
    }

    [Fact]
    public void TheGuardStillCatchesAValueThatMerelyPassesThroughAnOperator()
    {
        // The argument-position rule must not become an escape hatch of its own: `?? fallback` and
        // string concatenation both still hand the real value to the sink.
        ScanSnippet("""logger.LogInformation("{Name}", calendar.DisplayName ?? "unknown");""")
            .Should().NotBeEmpty("a null-coalescing fallback still logs the name when it is present");
        ScanSnippet("""logger.LogInformation("{Name}", calendar.DisplayName + " (primary)");""")
            .Should().NotBeEmpty("concatenation still logs the name");
        ScanSnippet("""logger.LogInformation("{Name}", calendar.DisplayName!);""")
            .Should().NotBeEmpty("a null-forgiving operator changes nothing about what is logged");
    }

    [Fact]
    public void TheGuardCoversTheWholeILoggerSurfaceNotOnlyTheLevelNamedExtensions()
    {
        ScanSnippet("""logger.Log(LogLevel.Information, "Syncing {Name}", calendar.DisplayName);""")
            .Should().NotBeEmpty("ILogger.Log is the same sink the LogInformation extension delegates to");
        ScanSnippet("""using var scope = logger.BeginScope("Location {PlaceName}", location.PlaceName);""")
            .Should().NotBeEmpty("a scope property attaches to every record emitted inside the scope");
        ScanSnippet("""throw new System.InvalidOperationException($"No results for '{placeName}'.");""")
            .Should().NotBeEmpty("a namespace-qualified exception type is the same exception");
    }

    [Fact]
    public void TheGuardCatchesDestructuringEvenThoughTheTemplateTextIsMasked()
    {
        // SKILL.md bans `{@Object}` outright. Every other check runs against the masked source, in
        // which the template text is blank — so this hazard needs the raw template or it can never
        // be seen at all.
        ScanSnippet("""logger.LogInformation("Syncing {@Calendar}", calendar);""")
            .Should().NotBeEmpty("destructuring writes DisplayName and GoogleCalendarId into the event");
        ScanSnippet("""logger.LogInformation("Syncing {CalendarInfoId}", calendar.Id);""")
            .Should().BeEmpty("a named placeholder logs only the field it names");
    }

    /// <summary>A value flagged by <see cref="Scan"/>, and where.</summary>
    private readonly record struct Violation(string Value, string Kind, string Reason, int Line);

    /// <summary>
    /// The whole pipeline, over one file or one snippet. Both the src/ sweep and the self-tests go
    /// through here, so a self-test cannot pass against a scanner the real run does not use.
    /// </summary>
    private static IReadOnlyList<Violation> Scan(string source)
    {
        var code = Mask(source);
        var violations = new List<Violation>();

        foreach (var (kind, region) in EnumerateSinkCallArguments(code))
        {
            foreach (var (value, reason, pattern) in BannedValues)
            {
                foreach (Match match in Regex.Matches(region.Text, pattern))
                {
                    if (!IsArgumentPosition(region.Text, match.Index + match.Length)) continue;

                    violations.Add(new Violation(
                        value, kind, reason, LineNumberAt(source, region.Start + match.Index)));
                }
            }

            if (kind == ExceptionKind) continue;

            // Positions are preserved by masking, so the same span of the ORIGINAL source is this
            // call's arguments with their message template still readable.
            var rawArguments = source.Substring(region.Start, region.Text.Length);
            foreach (Match match in DestructuringHole.Matches(rawArguments))
            {
                violations.Add(new Violation(
                    "a destructured object", kind, DestructuringReason,
                    LineNumberAt(source, region.Start + match.Index)));
            }
        }

        return violations;
    }

    /// <summary>Runs the same pipeline the guard runs, over one snippet, returning the values flagged.</summary>
    private static IReadOnlyList<string> ScanSnippet(string snippet) =>
        Scan(snippet).Select(v => v.Value).ToList();

    /// <summary>
    /// Blanks everything that is not code the sink actually receives: comments, string literal text,
    /// and the arguments of <c>Redact(…)</c> and <c>nameof(…)</c>.
    /// </summary>
    private static string Mask(string source) =>
        BlankNameofArguments(BlankRedactedArguments(MaskCommentsAndLiteralText(source)));

    /// <summary>
    /// True when the value matched immediately before <paramref name="index"/> is what the call
    /// RECEIVES, rather than something read while computing an argument.
    /// <para>
    /// The value is received when the next significant character closes it off — a comma, the end of
    /// the argument list, the <c>)</c> that <see cref="SkipHole"/> writes in place of an
    /// interpolation hole's closing brace, or the <c>}</c> of a collection/object initializer such
    /// as a <c>BeginScope</c> dictionary. A trailing null-forgiving <c>!</c> changes nothing, and
    /// <c>??</c> and <c>+</c> still hand the real value over, so all three are transparent.
    /// </para>
    /// </summary>
    private static bool IsArgumentPosition(string region, int index)
    {
        var i = index;

        // `x!` is the same value; `x != y` is a comparison whose RESULT is the argument.
        if (i < region.Length && region[i] == '!' && Next(region, i) != '=') i++;

        while (i < region.Length && char.IsWhiteSpace(region[i])) i++;

        if (i >= region.Length) return true;
        if (region[i] is ',' or ')' or '}') return true;

        return (region[i] == '?' && Next(region, i) == '?')
               || (region[i] == '+' && Next(region, i) is not ('+' or '='));
    }

    private readonly record struct SinkRegion(int Start, string Text);

    /// <summary>
    /// Every sink call's argument list, located by balanced-paren scan from its opening parenthesis.
    /// Nested calls are covered because the outer region contains them.
    /// </summary>
    private static IEnumerable<(string Kind, SinkRegion Region)> EnumerateSinkCallArguments(string code)
    {
        foreach (var (kind, start) in SinkCalls)
        {
            foreach (Match match in start.Matches(code))
            {
                var open = match.Index + match.Length - 1;
                var close = MatchingParenthesis(code, open);
                yield return (kind, new SinkRegion(open + 1, code[(open + 1)..close]));
            }
        }
    }

    /// <summary>
    /// Blanks the argument text of every <c>Redact(…)</c> call, so the sensitive value a caller
    /// deliberately routes through the redactor does not read as a leak.
    /// </summary>
    private static string BlankRedactedArguments(string code) => BlankArgumentsOf(RedactCall, code);

    /// <summary>
    /// Blanks the argument text of every <c>nameof(…)</c> call. <c>nameof</c> evaluates to the
    /// identifier, not the value behind it, so a guard clause naming a sensitive parameter discloses
    /// exactly nothing.
    /// </summary>
    private static string BlankNameofArguments(string code) => BlankArgumentsOf(NameofCall, code);

    private static string BlankArgumentsOf(Regex call, string code)
    {
        var masked = new StringBuilder(code);
        foreach (Match match in call.Matches(code))
        {
            var open = match.Index + match.Length - 1;
            Blank(masked, code, open + 1, MatchingParenthesis(code, open));
        }

        return masked.ToString();
    }

    /// <summary>Index of the <c>)</c> closing the <c>(</c> at <paramref name="open"/>, or end of input.</summary>
    private static int MatchingParenthesis(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')' && --depth == 0) return i;
        }

        return code.Length;
    }

    /// <summary>
    /// Blanks comments and the literal text of every string and char literal, preserving character
    /// positions and line breaks so match offsets still map to the original file's line numbers.
    /// The interpolation holes of a <c>$"…"</c> string are left intact: they are code, and they are
    /// where an exception message carries its values.
    /// </summary>
    private static string MaskCommentsAndLiteralText(string source)
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
            else if (StartsInterpolatedString(source, i))
            {
                i = MaskInterpolatedString(masked, source, i);
            }
            else if (c == '"' && Next(source, i) == '"' && Next(source, i + 1) == '"')
            {
                i = Blank(masked, source, i, EndOfRawString(source, i));
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

    /// <summary>True when <paramref name="index"/> begins a <c>$"</c>, <c>$@"</c> or <c>@$"</c> string.</summary>
    private static bool StartsInterpolatedString(string source, int index)
    {
        if (source[index] != '$' && !(source[index] == '@' && Next(source, index) == '$'))
        {
            return false;
        }

        var i = index;
        while (i < source.Length && source[i] is '$' or '@')
        {
            i++;
        }

        return i < source.Length && source[i] == '"';
    }

    /// <summary>
    /// Blanks an interpolated string's literal text while preserving its holes, and returns the
    /// index just past it. A raw interpolated string (<c>$"""…"""</c>) is blanked wholesale — the
    /// codebase has none, and guessing its hole-brace count would be more risk than value.
    /// </summary>
    private static int MaskInterpolatedString(StringBuilder masked, string source, int start)
    {
        var i = start;
        var verbatim = false;
        while (source[i] is '$' or '@')
        {
            verbatim |= source[i] == '@';
            Blank(masked, source, i, i + 1);
            i++;
        }

        if (Next(source, i) == '"' && Next(source, i + 1) == '"')
        {
            return Blank(masked, source, i, EndOfRawString(source, i));
        }

        i = Blank(masked, source, i, i + 1); // opening quote

        while (i < source.Length)
        {
            var c = source[i];

            if (c == '"')
            {
                // In a verbatim string "" escapes a quote; otherwise this closes the string.
                if (verbatim && Next(source, i) == '"')
                {
                    i = Blank(masked, source, i, i + 2);
                    continue;
                }

                return Blank(masked, source, i, i + 1);
            }

            if (!verbatim && c == '\\')
            {
                i = Blank(masked, source, i, i + 2);
                continue;
            }

            if (c is '{' or '}' && Next(source, i) == c)
            {
                i = Blank(masked, source, i, i + 2); // {{ and }} are escaped braces, not a hole
                continue;
            }

            if (c == '{')
            {
                i = SkipHole(masked, source, i);
                continue;
            }

            if (c == '\n' && !verbatim)
            {
                return i; // unterminated: stop at the line end rather than swallowing the file
            }

            i = Blank(masked, source, i, i + 1);
        }

        return i;
    }

    /// <summary>
    /// Leaves an interpolation hole's contents in place (they are code) and returns the index just
    /// past its closing brace.
    /// <para>
    /// The braces themselves become parentheses rather than blanks, so each hole reads as its own
    /// bracketed expression: <c>$"lat={latitude}, lon={longitude}"</c> masks to
    /// <c>      (latitude)      (longitude)</c>, where the argument-position test can see that
    /// <c>latitude</c> is handed over whole. Blanking them ran every hole of a message together into
    /// one run of identifiers, so only the last one looked like an argument. Parentheses also keep
    /// the enclosing call's paren balance intact, which is how the region was found.
    /// </para>
    /// </summary>
    private static int SkipHole(StringBuilder masked, string source, int open)
    {
        Blank(masked, source, open, open + 1);
        masked[open] = '(';

        var depth = 1;
        var i = open + 1;

        while (i < source.Length && depth > 0)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                var next = Blank(masked, source, i, i + 1);
                masked[i] = ')';
                return next;
            }

            i++;
        }

        return i;
    }

    private static char Next(string source, int index) =>
        index + 1 < source.Length ? source[index + 1] : '\0';

    /// <summary>Index just past a raw string literal's closing quote run.</summary>
    private static int EndOfRawString(string source, int start)
    {
        var fenceLength = 0;
        while (start + fenceLength < source.Length && source[start + fenceLength] == '"')
        {
            fenceLength++;
        }

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
    /// Index just past the closing quote of a regular string or char literal, where <c>\</c>
    /// escapes the next character. An unterminated literal stops at the end of the line.
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

    private static string LineTextAt(string source, int lineNumber)
    {
        var lines = source.Split('\n');
        return lineNumber >= 1 && lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : string.Empty;
    }

    private static IEnumerable<string> EnumerateProductionSources(string sourceRoot) =>
        Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        // EF-generated model snapshots name every mapped column, including the
                        // sensitive ones, and contain no logging at all.
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
