using FamilyHQ.Services.Auth;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

// FHQ-60: GrantsCalendar is the seam that decides whether a Google grant includes full calendar
// access. It must match the calendar scope as an exact space-delimited token — a near-miss like
// "calendar.readonly" must NOT count, and null/empty must fail safe to "not granted".
public class GoogleScopesTests
{
    [Theory]
    [InlineData("https://www.googleapis.com/auth/calendar")]
    [InlineData("openid email https://www.googleapis.com/auth/calendar")]
    [InlineData("https://www.googleapis.com/auth/calendar openid")]
    public void GrantsCalendar_WhenFullCalendarScopePresent_ReturnsTrue(string granted)
        => GoogleScopes.GrantsCalendar(granted).Should().BeTrue();

    [Theory]
    [InlineData("openid email")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("https://www.googleapis.com/auth/calendar.readonly")]
    [InlineData("https://www.googleapis.com/auth/calendar.events openid")]
    public void GrantsCalendar_WhenFullCalendarScopeAbsent_ReturnsFalse(string? granted)
        => GoogleScopes.GrantsCalendar(granted).Should().BeFalse();

    [Fact]
    public void Calendar_IsTheFullReadWriteScope()
        => GoogleScopes.Calendar.Should().Be("https://www.googleapis.com/auth/calendar");
}
