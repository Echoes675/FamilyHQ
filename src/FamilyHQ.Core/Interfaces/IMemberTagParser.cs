namespace FamilyHQ.Core.Interfaces;

public interface IMemberTagParser
{
    /// <summary>
    /// Extracts assigned member names from an event description.
    /// Checks for structured [members: Name1, Name2] tag first.
    /// Falls back to whole-word name matching against knownMemberNames.
    /// </summary>
    IReadOnlyList<string> ParseMembers(string? description, IReadOnlyList<string> knownMemberNames);

    /// <summary>
    /// Returns the description with the [members:...] tag removed (user-visible text only).
    /// </summary>
    string StripMemberTag(string? description);

    /// <summary>
    /// Replaces or inserts [members: Name1, Name2] in the description.
    /// Preserves any user-visible text outside the tag.
    /// </summary>
    string NormaliseDescription(string? description, IReadOnlyList<string> memberNames);
}
