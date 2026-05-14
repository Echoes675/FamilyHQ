using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.Components.Diagnostics;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Diagnostics;

public class DiagnosticsViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldShowEmptyState_NullList_ReturnsTrue()
    {
        DiagnosticsView.ShouldShowEmptyState(null).Should().BeTrue();
    }

    [Fact]
    public void ShouldShowEmptyState_EmptyList_ReturnsTrue()
    {
        DiagnosticsView.ShouldShowEmptyState(Array.Empty<SyncFailureDto>()).Should().BeTrue();
    }

    [Fact]
    public void ShouldShowEmptyState_NonEmpty_ReturnsFalse()
    {
        var list = new[]
        {
            new SyncFailureDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "gid",
                "title",
                "reason",
                "Exception",
                Now,
                false)
        };

        DiagnosticsView.ShouldShowEmptyState(list).Should().BeFalse();
    }

    [Fact]
    public void IsNeedsReauth_NullStatus_ReturnsFalse()
    {
        DiagnosticsView.IsNeedsReauth(null).Should().BeFalse();
    }

    [Fact]
    public void IsNeedsReauth_ActiveStatus_ReturnsFalse()
    {
        var status = new ConnectionStatusWithCalendarsDto("active", null, null, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.IsNeedsReauth(status).Should().BeFalse();
    }

    [Fact]
    public void IsNeedsReauth_NeedsReauthStatus_ReturnsTrue()
    {
        var status = new ConnectionStatusWithCalendarsDto("needs_reauth", "err", Now, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.IsNeedsReauth(status).Should().BeTrue();
    }

    [Fact]
    public void StatusBadgeLabel_NeedsReauth_ReturnsNeedsReauth()
    {
        var status = new ConnectionStatusWithCalendarsDto("needs_reauth", null, null, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.StatusBadgeLabel(status).Should().Be("Needs Reauth");
    }

    [Fact]
    public void StatusBadgeLabel_Active_ReturnsActive()
    {
        var status = new ConnectionStatusWithCalendarsDto("active", null, null, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.StatusBadgeLabel(status).Should().Be("Active");
    }

    [Fact]
    public void StatusBadgeCssClass_NeedsReauth_UsesWarningModifier()
    {
        var status = new ConnectionStatusWithCalendarsDto("needs_reauth", null, null, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.StatusBadgeCssClass(status).Should().Contain("diagnostics-status-badge--warning");
    }

    [Fact]
    public void StatusBadgeCssClass_Active_UsesOkModifier()
    {
        var status = new ConnectionStatusWithCalendarsDto("active", null, null, Array.Empty<ConnectionStatusCalendarDto>());
        DiagnosticsView.StatusBadgeCssClass(status).Should().Contain("diagnostics-status-badge--ok");
    }

    [Fact]
    public void FormatRelative_JustNow_WithinFortyFiveSeconds_ReturnsJustNow()
    {
        DiagnosticsView.FormatRelative(Now.AddSeconds(-10), Now).Should().Be("just now");
    }

    [Fact]
    public void FormatRelative_FutureTimestamp_ReturnsJustNow()
    {
        DiagnosticsView.FormatRelative(Now.AddSeconds(5), Now).Should().Be("just now");
    }

    [Fact]
    public void FormatRelative_Minutes_ReturnsMinutesAgo()
    {
        DiagnosticsView.FormatRelative(Now.AddMinutes(-3), Now).Should().Be("3m ago");
    }

    [Fact]
    public void FormatRelative_Hours_ReturnsHoursAgo()
    {
        DiagnosticsView.FormatRelative(Now.AddHours(-2), Now).Should().Be("2h ago");
    }

    [Fact]
    public void FormatRelative_Days_ReturnsDaysAgo()
    {
        DiagnosticsView.FormatRelative(Now.AddDays(-5), Now).Should().Be("5d ago");
    }

    [Fact]
    public void FormatRelative_OverThirtyDays_FallsBackToAbsoluteDate()
    {
        var when = Now.AddDays(-45);
        var result = DiagnosticsView.FormatRelative(when, Now);

        result.Should().Be(when.ToString("yyyy-MM-dd"));
    }
}
