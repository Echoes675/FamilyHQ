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
        var result = _sut.ParseMembers("Eoin and Sarah collecting the kids from football", KnownNames);
        result.Should().Contain("Eoin").And.Contain("Sarah");
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
