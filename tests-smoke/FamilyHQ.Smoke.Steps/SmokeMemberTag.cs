using System.Text.RegularExpressions;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// Reads FamilyHQ's managed <c>[members: Name1, Name2]</c> tag out of a Google description.
/// <para>
/// A deliberate re-implementation of the pattern rather than a reference to the product's parser: a smoke
/// assertion that used FamilyHQ's own parser to check FamilyHQ's own output would pass for any two
/// implementations that agree with each other, including two that are both wrong. The tag's shape is a
/// contract with the Google Calendar app — that is the thing being verified — so the suite states it
/// independently.
/// </para>
/// <para>
/// The set of names is compared, not the literal string: the tag's job is to say who the members are, and
/// the order Google happens to return them in is not part of that promise.
/// </para>
/// </summary>
public static class SmokeMemberTag
{
    private static readonly Regex TagPattern = new(
        @"\[members:\s*([^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The member names the tag carries, or an empty set when the description has no tag. Ordinal
    /// comparison, because the names are calendar display names and FamilyHQ writes them back verbatim.
    /// </summary>
    public static IReadOnlySet<string> NamesIn(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var match = TagPattern.Match(description);
        if (!match.Success)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
