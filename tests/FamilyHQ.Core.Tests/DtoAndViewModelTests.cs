using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.ViewModels;

namespace FamilyHQ.Core.Tests.DTOs;

public class DtoAndViewModelTests
{
    [Fact]
    public void CalendarEventDto_Initialization_SetPropertiesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var googleEventId = "google-id";
        var title = "Test Event";
        var start = DateTimeOffset.Now;
        var end = DateTimeOffset.Now.AddHours(1);
        var isAllDay = true;
        var location = "Test Location";
        var description = "Test Description";
        var calendars = new List<EventCalendarDto>
        {
            new(Guid.NewGuid(), "Work", "#ffffff")
        };

        // Act
        var dto = new CalendarEventDto(id, googleEventId, title, start, end, isAllDay, location, description, calendars);

        // Assert
        dto.Id.Should().Be(id);
        dto.GoogleEventId.Should().Be(googleEventId);
        dto.Title.Should().Be(title);
        dto.Start.Should().Be(start);
        dto.End.Should().Be(end);
        dto.IsAllDay.Should().Be(isAllDay);
        dto.Location.Should().Be(location);
        dto.Description.Should().Be(description);
        dto.Calendars.Should().BeEquivalentTo(calendars);
    }

    [Fact]
    public void MonthViewDto_Initialization_SetPropertiesCorrectly()
    {
        // Arrange
        var year = 2026;
        var month = 3;
        var calendarDto = new CalendarEventDto(
            Guid.NewGuid(), "google-id", "Test Event",
            DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1),
            false, null, null, []);
        var days = new Dictionary<string, List<CalendarEventDto>>
        {
            { "2026-03-09", new List<CalendarEventDto> { calendarDto } }
        };

        // Act
        var dto = new MonthViewDto
        {
            Year = year,
            Month = month,
            Days = days
        };

        // Assert
        dto.Year.Should().Be(year);
        dto.Month.Should().Be(month);
        dto.Days.Should().BeEquivalentTo(days);
    }

    [Fact]
    public void CalendarEventViewModel_Initialization_SetPropertiesCorrectly()
    {
        // Arrange
        var id = "string-id";
        var title = "View Model Title";
        var startTime = DateTime.Now;
        var endTime = DateTime.Now.AddHours(2);
        var isAllDay = false;
        var location = "Home";
        var calendarName = "Work Calendar";
        var calendarColor = "#ff0000";
        var calendarId = Guid.NewGuid();

        // Act
        var vm = new CalendarEventViewModel
        {
            Id = id,
            Title = title,
            StartTime = startTime,
            EndTime = endTime,
            IsAllDay = isAllDay,
            Location = location,
            CalendarName = calendarName,
            CalendarColor = calendarColor,
            CalendarId = calendarId
        };

        // Assert
        vm.Id.Should().Be(id);
        vm.Title.Should().Be(title);
        vm.StartTime.Should().Be(startTime);
        vm.EndTime.Should().Be(endTime);
        vm.IsAllDay.Should().Be(isAllDay);
        vm.Location.Should().Be(location);
        vm.CalendarName.Should().Be(calendarName);
        vm.CalendarColor.Should().Be(calendarColor);
        vm.CalendarId.Should().Be(calendarId);
    }
}
