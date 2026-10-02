using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// Pins the one boundary this filter exists for: whether a ping exactly at "now" has fired. Getting
/// this wrong by a single comparison operator either drops a row a minute before its phone actually
/// rings, or leaves a fired one showing for an extra minute — and it has to agree with the server's
/// own <c>TriggerAt &gt;= now</c> filter in <c>RemindersController.GetUpcoming</c>, not just with
/// itself.
/// </summary>
public class ReminderTickFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<DateTimeOffset> DropFired(params DateTimeOffset[] pings) =>
        ReminderTickFilter.DropFired(pings, p => p, Now);

    [Fact]
    public void DropFired_PingOneMinuteBeforeNow_IsDropped()
    {
        var result = DropFired(Now.AddMinutes(-1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void DropFired_PingExactlyAtNow_IsKept()
    {
        // The boundary itself: the server's own filter (TriggerAt >= now) has not fired this ping
        // yet, so the client re-filing the same list on a later tick must not disagree with it.
        var result = DropFired(Now);

        result.Should().ContainSingle().Which.Should().Be(Now);
    }

    [Fact]
    public void DropFired_PingOneMinuteAfterNow_IsKept()
    {
        var oneMinuteLater = Now.AddMinutes(1);

        var result = DropFired(oneMinuteLater);

        result.Should().ContainSingle().Which.Should().Be(oneMinuteLater);
    }

    [Fact]
    public void DropFired_NeverMutatesTheSourceList()
    {
        var pings = new[] { Now.AddMinutes(-1), Now.AddMinutes(1) };

        ReminderTickFilter.DropFired(pings, p => p, Now);

        pings.Should().HaveCount(2, "the source array must be untouched, not just the returned list");
    }

    [Fact]
    public void DropFired_WithNoPings_ReturnsEmptyNotNull()
    {
        var result = DropFired();

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
