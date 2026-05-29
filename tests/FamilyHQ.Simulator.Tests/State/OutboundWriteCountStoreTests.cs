using FamilyHQ.Simulator.State;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Simulator.Tests.State;

public class OutboundWriteCountStoreTests
{
    [Fact]
    public void Total_NoWrites_ReturnsZero()
    {
        var sut = new OutboundWriteCountStore();

        sut.Total().Should().Be(0);
    }

    [Fact]
    public void Total_SumsCountsAcrossAllEventIds()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("evt-a");
        sut.Increment("evt-a");
        sut.Increment("evt-b");

        sut.Total().Should().Be(3, "the total is the sum of every per-event write count");
    }

    [Fact]
    public void Total_AfterReset_ReturnsZero()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("evt-a");
        sut.Increment("evt-b");

        sut.Reset();

        sut.Total().Should().Be(0, "Reset clears every counter so the total returns to zero");
    }

    [Fact]
    public void Total_MatchesSingleEventGet_WhenOnlyOneEventWritten()
    {
        var sut = new OutboundWriteCountStore();
        sut.Increment("evt-only");

        sut.Total().Should().Be(sut.Get("evt-only"));
    }
}
