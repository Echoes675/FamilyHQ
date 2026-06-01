namespace FamilyHQ.Core.Interfaces;

public interface IMemberTagParser
{
    /// <summary>
    /// Extracts assigned member names from an event description.
    /// Checks for a structured [members: Name1, Name2] tag first; falls back to whole-word name
    /// matching against <paramref name="knownMemberNames"/> for free-form descriptions a user typed
    /// in any format/separator/case (e.g. "lunch with Alice and Bob", "Alice; Bob", "ALICE, bob").
    ///
    /// <para><paramref name="knownMemberNames"/> is the set of <b>member</b> (non-shared) calendar
    /// names — it governs the free-form fallback, so a description that merely mentions the shared
    /// (container) calendar's name is never treated as a membership.</para>
    ///
    /// <para><paramref name="taggedCalendarNames"/> (optional) is the candidate set for resolving an
    /// <b>explicit</b> [members:] tag. An explicit tag is authoritative, so it resolves against this
    /// broader set (typically <i>all</i> calendars, including a currently-shared one) — this is what
    /// keeps a tagged member from being dropped while its calendar is transiently shared (FHQ-46).
    /// When null, the tag resolves against <paramref name="knownMemberNames"/> (legacy behaviour).</para>
    /// </summary>
    IReadOnlyList<string> ParseMembers(
        string? description,
        IReadOnlyList<string> knownMemberNames,
        IReadOnlyList<string>? taggedCalendarNames = null);

    /// <summary>
    /// Extracts member names ONLY from an explicit [members: Name1, Name2] tag, with NO whole-word
    /// fallback. Returns an empty list when the description carries no such tag — used to decide
    /// whether a request is genuinely changing the member set, where plain text that merely mentions
    /// a member's name must not be treated as a membership change.
    /// </summary>
    IReadOnlyList<string> ExtractTaggedMembers(string? description, IReadOnlyList<string> knownMemberNames);

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
