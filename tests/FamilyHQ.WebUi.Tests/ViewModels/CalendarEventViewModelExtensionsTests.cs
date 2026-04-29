using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.ViewModels;

public class CalendarEventViewModelExtensionsTests
{
    #region StartLocal

    [Fact]
    public void StartLocal_WhenStartIsUtc_ReturnsLocalWallClock()
    {
        // Arrange
        var utcStart = new DateTimeOffset(2026, 4, 29, 13, 0, 0, TimeSpan.Zero);
        var expected = TimeZoneInfo.ConvertTime(utcStart, TimeZoneInfo.Local).DateTime;
        var evt = CreateEvent(start: utcStart, end: utcStart.AddHours(1));

        // Act
        var result = evt.StartLocal();

        // Assert
        result.Should().Be(expected);
        result.Kind.Should().Be(DateTimeKind.Local);
    }

    [Fact]
    public void StartLocal_WhenStartHasNonZeroOffset_ReturnsLocalWallClock()
    {
        // Arrange
        var start = new DateTimeOffset(2026, 4, 29, 13, 0, 0, TimeSpan.FromHours(2));
        var expected = TimeZoneInfo.ConvertTime(start, TimeZoneInfo.Local).DateTime;
        var evt = CreateEvent(start: start, end: start.AddHours(1));

        // Act
        var result = evt.StartLocal();

        // Assert
        result.Should().Be(expected);
        result.Kind.Should().Be(DateTimeKind.Local);
    }

    [Fact]
    public void StartLocal_DifferenceFromUtc_EqualsLocalUtcOffset()
    {
        // Arrange
        var utcStart = new DateTimeOffset(2026, 4, 29, 13, 0, 0, TimeSpan.Zero);
        var evt = CreateEvent(start: utcStart, end: utcStart.AddHours(1));
        var expectedOffsetHours = TimeZoneInfo.Local.GetUtcOffset(utcStart.UtcDateTime).TotalHours;

        // Act
        var result = evt.StartLocal();

        // Assert
        (result - utcStart.UtcDateTime).TotalHours.Should().Be(expectedOffsetHours);
    }

    #endregion

    #region EndLocal

    [Fact]
    public void EndLocal_WhenEndIsUtc_ReturnsLocalWallClock()
    {
        // Arrange
        var utcEnd = new DateTimeOffset(2026, 4, 29, 14, 0, 0, TimeSpan.Zero);
        var expected = TimeZoneInfo.ConvertTime(utcEnd, TimeZoneInfo.Local).DateTime;
        var evt = CreateEvent(start: utcEnd.AddHours(-1), end: utcEnd);

        // Act
        var result = evt.EndLocal();

        // Assert
        result.Should().Be(expected);
        result.Kind.Should().Be(DateTimeKind.Local);
    }

    [Fact]
    public void EndLocal_WhenEndHasNonZeroOffset_ReturnsLocalWallClock()
    {
        // Arrange
        var end = new DateTimeOffset(2026, 4, 29, 14, 0, 0, TimeSpan.FromHours(2));
        var expected = TimeZoneInfo.ConvertTime(end, TimeZoneInfo.Local).DateTime;
        var evt = CreateEvent(start: end.AddHours(-1), end: end);

        // Act
        var result = evt.EndLocal();

        // Assert
        result.Should().Be(expected);
        result.Kind.Should().Be(DateTimeKind.Local);
    }

    [Fact]
    public void EndLocal_DifferenceFromUtc_EqualsLocalUtcOffset()
    {
        // Arrange
        var utcEnd = new DateTimeOffset(2026, 4, 29, 14, 0, 0, TimeSpan.Zero);
        var evt = CreateEvent(start: utcEnd.AddHours(-1), end: utcEnd);
        var expectedOffsetHours = TimeZoneInfo.Local.GetUtcOffset(utcEnd.UtcDateTime).TotalHours;

        // Act
        var result = evt.EndLocal();

        // Assert
        (result - utcEnd.UtcDateTime).TotalHours.Should().Be(expectedOffsetHours);
    }

    #endregion

    #region StartLocal vs Start.Date asymmetry (cross-midnight UTC vs local)

    [Fact]
    public void StartLocal_AtUtcMidnightBoundary_UsesLocalDate_WhileStartDate_UsesUtcDate()
    {
        // Arrange — 23:30 UTC on 29 Jun 2026 is 00:30 BST on 30 Jun 2026.
        var start = new DateTimeOffset(2026, 6, 29, 23, 30, 0, TimeSpan.Zero);
        var evt = CreateEvent(start: start, end: start.AddMinutes(30));

        // Act
        var localDate = evt.StartLocal().Date;
        var rawDate = evt.Start.Date;

        // Assert — locks in the asymmetry: helper must apply the local offset.
        var expectedLocalDate = TimeZoneInfo.ConvertTime(start, TimeZoneInfo.Local).Date;
        localDate.Should().Be(expectedLocalDate);
        rawDate.Should().Be(new DateTime(2026, 6, 29));
    }

    #endregion

    private static CalendarEventViewModel CreateEvent(DateTimeOffset start, DateTimeOffset end) =>
        new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title: "Test Event",
            Start: start,
            End: end,
            IsAllDay: false,
            Location: "Somewhere",
            Description: null,
            CalendarInfoId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CalendarDisplayName: "Family",
            CalendarColor: "#ff0000",
            AllCalendars: Array.Empty<CalendarSummaryViewModel>());
}
