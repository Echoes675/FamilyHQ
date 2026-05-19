using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.Services.Tests.Calendar;

public class OutboundWriteHashCacheTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-05-19T12:00:00Z"));

    private OutboundWriteHashCache CreateSut() => new(_clock);

    [Fact]
    public void Record_then_WasRecentlyWritten_returns_true_immediately()
    {
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        sut.WasRecentlyWritten("evt-1", "hash-A").Should().BeTrue();
    }

    [Fact]
    public void WasRecentlyWritten_returns_false_after_TTL_expires()
    {
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        _clock.Advance(TimeSpan.FromSeconds(61));
        sut.WasRecentlyWritten("evt-1", "hash-A").Should().BeFalse();
    }

    [Fact]
    public void WasRecentlyWritten_returns_false_for_unknown_pair()
    {
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        sut.WasRecentlyWritten("evt-2", "hash-A").Should().BeFalse();
        sut.WasRecentlyWritten("evt-1", "hash-B").Should().BeFalse();
    }

    [Fact]
    public void WasRecentlyWritten_returns_false_for_same_id_with_different_hash()
    {
        // A legitimate concurrent edit in Google produces a different hash — guard must NOT skip.
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        sut.WasRecentlyWritten("evt-1", "hash-B").Should().BeFalse();
    }

    [Fact]
    public void Record_is_idempotent_for_same_pair()
    {
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        _clock.Advance(TimeSpan.FromSeconds(30));
        sut.Record("evt-1", "hash-A");                 // refresh the entry
        _clock.Advance(TimeSpan.FromSeconds(40));      // total 70s since first record but only 40s since refresh
        sut.WasRecentlyWritten("evt-1", "hash-A").Should().BeTrue();
    }

    [Fact]
    public void Concurrent_Record_and_WasRecentlyWritten_do_not_throw()
    {
        var sut = CreateSut();
        var ids = Enumerable.Range(0, 200).Select(i => ($"evt-{i}", $"hash-{i}")).ToArray();

        Parallel.For(0, ids.Length, i =>
        {
            sut.Record(ids[i].Item1, ids[i].Item2);
            sut.WasRecentlyWritten(ids[i].Item1, ids[i].Item2).Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("", "hash")]
    [InlineData("evt", null)]
    [InlineData("evt", "")]
    public void Record_throws_for_null_or_empty_args(string? id, string? hash)
    {
        var sut = CreateSut();
        var act = () => sut.Record(id!, hash!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("", "hash")]
    [InlineData("evt", null)]
    [InlineData("evt", "")]
    public void WasRecentlyWritten_returns_false_for_null_or_empty_args(string? id, string? hash)
    {
        var sut = CreateSut();
        sut.Record("evt-1", "hash-A");
        sut.WasRecentlyWritten(id!, hash!).Should().BeFalse();
    }
}
