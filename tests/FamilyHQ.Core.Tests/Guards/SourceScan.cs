using System.Text;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Guards;

/// <summary>
/// The shared machinery behind every lexical guard test in this project —
/// <see cref="PiiInLogsGuardTests"/>, <see cref="OutboundZoneGuardTests"/>,
/// <see cref="UnitTestPurityGuardTests"/> and <see cref="DateOnlyParseGuardTests"/>.
/// <para>
/// It exists because there were three near-identical copies of it and a fourth, cruder one was about
/// to be added (FHQ-174). Copies drift: the crude one blanked whole-line comments only, so a
/// <c>const string</c> naming the construct it looks for failed the build — the "trains reviewers to
/// add suppressions" outcome the guards are supposed to avoid. One implementation, one set of
/// known limits, fixed in one place.
/// </para>
/// <para>
/// <b>What masking buys.</b> A guard that greps raw source fires on its own documentation. Every
/// masker here preserves character positions and line breaks, so an offset into the masked text
/// still maps to the original file's line number.
/// </para>
/// <para>
/// <b>What it does not.</b> It is a lexer's approximation, not a parser. Constructs reached through
/// a local, a constant, an alias or reflection are invisible to every caller; a raw INTERPOLATED
/// string (<c>$"""…"""</c>) is blanked wholesale rather than hole by hole; and a non-verbatim
/// interpolated string containing an escaped quote may be over- or under-blanked by a few
/// characters. These raise the cost of a regression; they do not make one impossible.
/// </para>
/// </summary>
internal static class SourceScan
{
    /// <summary>The file whose presence identifies the repository root.</summary>
    public const string RepositoryMarker = "FamilyHQ.slnx";

    /// <summary>
    /// Blanks comments and the contents of every string and char literal, interpolation holes
    /// included. For a guard that reads code positions — assignments, argument names, call sites —
    /// none of which can live inside a string.
    /// </summary>
    public static string MaskCommentsAndLiterals(string source) =>
        Mask(source, preserveInterpolationHoles: false);

    /// <summary>
    /// Blanks comments and the literal TEXT of every string and char literal, but keeps the
    /// interpolation holes of a <c>$"…"</c> string: they are code, and they are where an exception
    /// message carries its values. Each hole's braces become parentheses so it reads as its own
    /// bracketed expression and the enclosing call's paren balance is preserved.
    /// </summary>
    public static string MaskCommentsAndLiteralText(string source) =>
        Mask(source, preserveInterpolationHoles: true);

    private static string Mask(string source, bool preserveInterpolationHoles)
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
            else if (preserveInterpolationHoles && StartsInterpolatedString(source, i))
            {
                i = MaskInterpolatedString(masked, source, i);
            }
            else if (c == '"' && Next(source, i) == '"' && Next(source, i + 1) == '"')
            {
                i = Blank(masked, source, i, EndOfRawString(source, i));
            }
            else if ((c is '@' or '$') && StartsQuoted(source, i, out var quote))
            {
                // The prefix run decides how the literal terminates, and getting it wrong makes the
                // guard BLIND, not merely noisy. `$"a \" b"` is escape-honouring: lexing it as
                // verbatim ends the literal at the escaped quote, and the real closing quote then
                // opens a fresh one that blanks the rest of the LINE — taking any banned construct
                // after it with it. Only an `@` in the prefix means verbatim.
                var isRaw = Next(source, quote) == '"' && Next(source, quote + 1) == '"';
                var isVerbatim = source.AsSpan(i, quote - i).IndexOf('@') >= 0;

                var end = isRaw
                    ? EndOfRawString(source, quote)
                    : isVerbatim
                        ? EndOfVerbatimString(source, quote + 1)
                        : EndOfSimpleLiteral(source, quote + 1, source[quote]);

                i = Blank(masked, source, i, end);
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
    /// <paramref name="quote"/> set to the opening quote's index.
    /// </summary>
    private static bool StartsQuoted(string source, int index, out int quote)
    {
        var i = index;
        while (i < source.Length && source[i] is '$' or '@') i++;

        quote = i;
        return i < source.Length && source[i] == '"';
    }

    /// <summary>True when <paramref name="index"/> begins a <c>$"</c>, <c>$@"</c> or <c>@$"</c> string.</summary>
    private static bool StartsInterpolatedString(string source, int index)
    {
        if (source[index] != '$' && !(source[index] == '@' && Next(source, index) == '$'))
        {
            return false;
        }

        return StartsQuoted(source, index, out _);
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
    /// past its closing brace. The braces become parentheses rather than blanks, so each hole reads
    /// as its own bracketed expression: <c>$"lat={latitude}, lon={longitude}"</c> masks to
    /// <c>      (latitude)      (longitude)</c>. Blanking them ran every hole of a message together
    /// into one run of identifiers, so only the last one looked like an argument.
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

    public static char Next(string source, int index) =>
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
    public static int Blank(StringBuilder target, string source, int start, int end)
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

    /// <summary>Index of the <c>)</c> closing the <c>(</c> at <paramref name="open"/>, or end of input.</summary>
    public static int MatchingParenthesis(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '(') depth++;
            else if (code[i] == ')' && --depth == 0) return i;
        }

        return code.Length;
    }

    public static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }

        return line;
    }

    public static string LineTextAt(string source, int lineNumber)
    {
        var lines = source.Split('\n');
        return lineNumber >= 1 && lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : string.Empty;
    }

    /// <summary>
    /// Every source file under <paramref name="root"/> with one of <paramref name="extensions"/>,
    /// excluding build output. <c>.razor</c> is a first-class extension here: a Blazor component's
    /// <c>@code</c> block is product C# and has carried at least one of these defects.
    /// </summary>
    public static IEnumerable<string> EnumerateSources(string root, params string[] extensions) =>
        extensions
            .SelectMany(extension => Directory.EnumerateFiles(root, $"*{extension}", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static string FindRepositoryRoot()
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
