using FamilyHQ.Simulator.State;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Simulator.Tests.State;

public class OutboundWriteCountStoreTests
{
    [Fact]
    public void TotalForUser_NoWrites_ReturnsZero()
    {
        var sut = new OutboundWriteCountStore();

        sut.TotalForUser("alice").Should().Be(0);
    }

    [Fact]
    public void TotalForUser_SumsWritesForThatUserOnly()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("alice", "evt-a");
        sut.Increment("alice", "evt-a");
        sut.Increment("alice", "evt-b");
        sut.Increment("bob", "evt-c");

        sut.TotalForUser("alice").Should().Be(3, "alice made three writes");
        sut.TotalForUser("bob").Should().Be(1, "a concurrent user's writes must not leak into alice's total");
    }

    [Fact]
    public void Get_TracksPerEventCountIndependentOfUser()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("alice", "evt-a");
        sut.Increment("alice", "evt-a");

        sut.Get("evt-a").Should().Be(2);
    }

    [Fact]
    public void Increment_NullOrEmptyUser_StillCountsPerEvent_ButNotPerUser()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment(null, "evt-a");

        sut.Get("evt-a").Should().Be(1);
        sut.TotalForUser("").Should().Be(0, "a null/empty user id contributes no per-user total");
    }

    [Fact]
    public void Reset_ClearsPerEventAndPerUserCounts()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("alice", "evt-a");

        sut.Reset();

        sut.Get("evt-a").Should().Be(0);
        sut.TotalForUser("alice").Should().Be(0);
    }
}
