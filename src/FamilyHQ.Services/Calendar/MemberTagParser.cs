using System.Text.RegularExpressions;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Calendar;

public class MemberTagParser : IMemberTagParser
{
    // Matches [members: Name1, Name2] anywhere in the string (case-insensitive).
    private static readonly Regex TagRegex = new(
        @"\[members:\s*([^\]]*)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> ParseMembers(
        string? description,
        IReadOnlyList<string> knownMemberNames,
        IReadOnlyList<string>? taggedCalendarNames = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var tagMatch = TagRegex.Match(description);
        if (tagMatch.Success)
            // An explicit tag is authoritative: resolve it against the broader candidate set (all
            // calendars, incl. a transiently-shared one) when supplied, so a tagged member is not
            // dropped while its calendar is shared (FHQ-46). Falls back to the member set when null.
            return ResolveTagContent(tagMatch.Groups[1].Value, taggedCalendarNames ?? knownMemberNames);

        // Free-form fallback: whole-word, case-insensitive, any separator/format (e.g. "Alice and
        // Bob", "alice; bob"). Matches ONLY member (non-shared) calendar names, so a description that
        // merely mentions the shared container calendar's name does not silently make it a member.
        return knownMemberNames
            .Where(name => Regex.IsMatch(description, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
            .ToList();
    }

    public IReadOnlyList<string> ExtractTaggedMembers(string? description, IReadOnlyList<string> knownMemberNames)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var tagMatch = TagRegex.Match(description);
        return tagMatch.Success
            ? ResolveTagContent(tagMatch.Groups[1].Value, knownMemberNames)
            : [];
    }

    private static List<string> ResolveTagContent(string tagContent, IReadOnlyList<string> knownMemberNames) =>
        tagContent
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => knownMemberNames.FirstOrDefault(
                k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

    public string StripMemberTag(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var stripped = TagRegex.Replace(description, string.Empty);
        return stripped.Trim();
    }

    public string NormaliseDescription(string? description, IReadOnlyList<string> memberNames)
    {
        var tag = $"[members: {string.Join(", ", memberNames)}]";
        var stripped = StripMemberTag(description);

        return string.IsNullOrWhiteSpace(stripped)
            ? tag
            : $"{stripped}\n{tag}";
    }
}
