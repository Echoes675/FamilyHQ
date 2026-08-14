using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class MemberTagParserTests
{
    private static readonly string[] KnownNames = ["Eoin", "Sarah", "Kids"];
    private readonly MemberTagParser _sut = new();

    // ── ParseMembers ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseMembers_StructuredTag_ReturnsTaggedMembers()
    {
        var result = _sut.ParseMembers("Dentist appointment [members: Eoin, Sarah]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah"]);
    }

    [Fact]
    public void ParseMembers_StructuredTagOnly_ReturnsTaggedMembers()
    {
        var result = _sut.ParseMembers("[members: Kids]", KnownNames);
        result.Should().BeEquivalentTo(["Kids"]);
    }

    [Fact]
    public void ParseMembers_NoTag_FallsBackToNameMatching()
    {
        // "kids" case-insensitively matches the known member name "Kids" — accepted as low-risk false positive.
        var result = _sut.ParseMembers("Eoin and Sarah collecting the kids from football", KnownNames);
        result.Should().Contain("Eoin").And.Contain("Sarah").And.Contain("Kids");
    }

    [Fact]
    public void ParseMembers_NoTagNoNameMatch_ReturnsEmpty()
    {
        var result = _sut.ParseMembers("Grocery shopping", KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseMembers_NullDescription_ReturnsEmpty()
    {
        var result = _sut.ParseMembers(null, KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseMembers_NameMatchIsWholeWordOnly()
    {
        // "Eoins" should not match "Eoin"
        var result = _sut.ParseMembers("Eoins car service", KnownNames);
        result.Should().NotContain("Eoin");
    }

    [Fact]
    public void ParseMembers_StructuredTagIgnoresCase()
    {
        var result = _sut.ParseMembers("[members: eoin, SARAH]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah"]);
    }

    [Fact]
    public void ParseMembers_StructuredTagWithUnknownName_IgnoresUnknown()
    {
        var result = _sut.ParseMembers("[members: Eoin, Unknown]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin"]);
    }

    // ── FHQ-75: an empty explicit tag is treated as "no tag present" ──────────
    //    "[members: ]" (and whitespace/comma-only variants) must fall through to free-form
    //    name matching instead of authoritatively wiping the event's membership.

    [Fact]
    public void ParseMembers_EmptyTag_FallsBackToNameMatching()
    {
        var result = _sut.ParseMembers("Eoin and Sarah are free [members: ]", KnownNames);
        result.Should().Contain("Eoin").And.Contain("Sarah");
    }

    [Fact]
    public void ParseMembers_EmptyTagWithoutSpace_FallsBackToNameMatching()
    {
        var result = _sut.ParseMembers("Lunch with Sarah [members:]", KnownNames);
        result.Should().BeEquivalentTo(["Sarah"]);
    }

    [Fact]
    public void ParseMembers_WhitespaceOnlyTag_FallsBackToNameMatching()
    {
        var result = _sut.ParseMembers("Kids football practice [members:   ]", KnownNames);
        result.Should().BeEquivalentTo(["Kids"]);
    }

    [Fact]
    public void ParseMembers_CommaOnlyTag_FallsBackToNameMatching()
    {
        // A tag containing only separators carries no member content — same as an empty tag.
        var result = _sut.ParseMembers("Dinner with Eoin [members: , ]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin"]);
    }

    [Fact]
    public void ParseMembers_EmptyTagNoNameMatch_ReturnsEmpty()
    {
        // Fall-through lands on free-form matching, which finds nothing here.
        var result = _sut.ParseMembers("Grocery shopping [members: ]", KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseMembers_TagWithOnlyUnknownName_IsAuthoritativeAndReturnsEmpty()
    {
        // Pins the boundary of the FHQ-75 fall-through: a tag WITH content stays authoritative
        // even when no name resolves — it must NOT fall through and match "Eoin" free-form.
        var result = _sut.ParseMembers("Dinner with Eoin [members: Bogus]", KnownNames);
        result.Should().BeEmpty();
    }

    // ── FHQ-46: explicit tag is authoritative (resolves against taggedCalendarNames); ────────────
    //    free-form fallback only ever matches member (non-shared) names. "Family" models the shared
    //    container calendar — present in the all-calendars tag set but NOT in the member set.
    private static readonly string[] AllNames = ["Eoin", "Sarah", "Kids", "Family"];

    [Fact]
    public void ParseMembers_ExplicitTag_ResolvesAgainstTaggedCalendarNames()
    {
        // "Family" is not a member name but IS in the authoritative tag-candidate set, so an explicit
        // tag naming it resolves it — this is what keeps a tagged member from being dropped while its
        // calendar is transiently shared.
        var result = _sut.ParseMembers("[members: Eoin, Family]", KnownNames, AllNames);
        result.Should().BeEquivalentTo(["Eoin", "Family"]);
    }

    [Fact]
    public void ParseMembers_FreeForm_DoesNotMatchSharedOnlyName()
    {
        // No tag → free-form fallback scans member names only. A description that merely mentions the
        // shared calendar's name ("Family") must NOT make it a member, even though it is in AllNames.
        var result = _sut.ParseMembers("Family movie night with Eoin", KnownNames, AllNames);
        result.Should().Contain("Eoin").And.NotContain("Family");
    }

    [Fact]
    public void ParseMembers_FreeForm_PreservesAnyFormatSeparatorAndCase()
    {
        // The user types member names however they like; the fallback matches whole-word,
        // case-insensitively, regardless of separator.
        var result = _sut.ParseMembers("EOIN & sarah; plus the KIDS", KnownNames, AllNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah", "Kids"]);
    }

    // ── ExtractTaggedMembers (explicit tag only, no fallback) ─────────────────

    [Fact]
    public void ExtractTaggedMembers_StructuredTag_ReturnsTaggedMembers()
    {
        var result = _sut.ExtractTaggedMembers("Dentist [members: Eoin, Sarah]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah"]);
    }

    [Fact]
    public void ExtractTaggedMembers_PlainTextMentioningNames_ReturnsEmpty()
    {
        // Unlike ParseMembers, there is NO whole-word fallback: plain text naming members is not a tag.
        var result = _sut.ExtractTaggedMembers("Eoin and Sarah collecting the kids", KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTaggedMembers_NullDescription_ReturnsEmpty()
    {
        var result = _sut.ExtractTaggedMembers(null, KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTaggedMembers_StructuredTagIgnoresCaseAndUnknowns()
    {
        var result = _sut.ExtractTaggedMembers("[members: eoin, Unknown]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin"]);
    }

    // ── StripMemberTag ────────────────────────────────────────────────────────

    [Fact]
    public void StripMemberTag_RemovesTagLeavesRest()
    {
        var result = _sut.StripMemberTag("Dentist [members: Eoin, Sarah]");
        result.Trim().Should().Be("Dentist");
    }

    [Fact]
    public void StripMemberTag_NoTag_ReturnsOriginal()
    {
        var result = _sut.StripMemberTag("Dentist appointment");
        result.Should().Be("Dentist appointment");
    }

    [Fact]
    public void StripMemberTag_NullReturnsEmpty()
    {
        var result = _sut.StripMemberTag(null);
        result.Should().BeEmpty();
    }

    // ── NormaliseDescription ──────────────────────────────────────────────────

    [Fact]
    public void NormaliseDescription_InsertsTagWhenAbsent()
    {
        var result = _sut.NormaliseDescription("Dentist", ["Eoin"]);
        result.Should().Be("Dentist\n[members: Eoin]");
    }

    [Fact]
    public void NormaliseDescription_ReplacesExistingTag()
    {
        var result = _sut.NormaliseDescription("Dentist [members: Eoin]", ["Eoin", "Sarah"]);
        result.Should().Be("Dentist\n[members: Eoin, Sarah]");
    }

    [Fact]
    public void NormaliseDescription_EmptyMemberList_ProducesTagWithNoNames()
    {
        var result = _sut.NormaliseDescription(null, []);
        result.Should().Be("[members: ]");
    }
}
