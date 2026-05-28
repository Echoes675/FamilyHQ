using System.Text.RegularExpressions;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Calendar;

public class MemberTagParser : IMemberTagParser
{
    // Matches [members: Name1, Name2] anywhere in the string (case-insensitive).
    private static readonly Regex TagRegex = new(
        @"\[members:\s*([^\]]*)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> ParseMembers(string? description, IReadOnlyList<string> knownMemberNames)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var tagMatch = TagRegex.Match(description);
        if (tagMatch.Success)
            return ResolveTagContent(tagMatch.Groups[1].Value, knownMemberNames);

        // Fallback: whole-word name matching
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
