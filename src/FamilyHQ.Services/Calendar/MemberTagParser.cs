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
        {
            var tagContent = tagMatch.Groups[1].Value;
            return tagContent
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => knownMemberNames.FirstOrDefault(
                    k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();
        }

        // Fallback: whole-word name matching
        return knownMemberNames
            .Where(name => Regex.IsMatch(description, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
            .ToList();
    }

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
