using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class EventContentHashTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End   = new(2026, 4, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_SameInputs_ReturnsSameHash()
    {
        var h1 = EventContentHash.Compute("Title", Start, End, false, "desc");
        var h2 = EventContentHash.Compute("Title", Start, End, false, "desc");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_DifferentTitle_ReturnsDifferentHash()
    {
        var h1 = EventContentHash.Compute("Title A", Start, End, false, null);
        var h2 = EventContentHash.Compute("Title B", Start, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_DifferentStartTime_ReturnsDifferentHash()
    {
        var later = Start.AddHours(1);
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", later, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_NullVsEmptyDescription_AreEquivalent()
    {
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", Start, End, false, "");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_ReturnsNonEmptyLowercaseHexString()
    {
        var hash = EventContentHash.Compute("T", Start, End, false, null);
        hash.Should().NotBeNullOrEmpty();
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void Compute_StartInDifferentTimezone_SameUtcMoment_ReturnsSameHash()
    {
        var startUtc   = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
        var startLocal = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.FromHours(1)); // same UTC moment
        var h1 = EventContentHash.Compute("T", startUtc,   End, false, null);
        var h2 = EventContentHash.Compute("T", startLocal, End, false, null);
        h1.Should().Be(h2);
    }
}
