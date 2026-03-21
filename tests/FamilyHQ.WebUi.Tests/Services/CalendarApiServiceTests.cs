using System.Net;
using System.Text.Json;
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.ViewModels;
using Moq;
using Moq.Protected;

namespace FamilyHQ.WebUi.Tests.Services;

public class CalendarApiServiceTests
{
    private static readonly Guid CalAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private const string FixedDateKey = "2026-03-21";
    private static readonly DateTimeOffset FixedStart = new(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetEventsForMonthAsync_ExpandsMultiCalendarEventIntoTwoViewModels()
    {
        // Arrange — one CalendarEventDto with 2 calendars → 2 CalendarEventViewModels in Days
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var calB = new EventCalendarDto(CalBId, "Cal B", "#0000ff");

        var dto = new CalendarEventDto(
            EventId,
            "gid-1",
            "Team Meeting",
            FixedStart,
            FixedEnd,
            false,
            null,
            null,
            [calA, calB]);

        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [FixedDateKey] = [dto] }
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        result.Days.Should().ContainKey(FixedDateKey);
        var vms = result.Days[FixedDateKey];
        vms.Should().HaveCount(2, "one ViewModel per calendar");
        vms.Should().Contain(v => v.CalendarInfoId == CalAId && v.CalendarColor == "#ff0000");
        vms.Should().Contain(v => v.CalendarInfoId == CalBId && v.CalendarColor == "#0000ff");
    }

    [Fact]
    public async Task GetEventsForMonthAsync_AllCalendarsPopulatedOnEachExpansion()
    {
        // Arrange — AllCalendars must contain both calendars on every expanded ViewModel
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var calB = new EventCalendarDto(CalBId, "Cal B", "#0000ff");

        var dto = new CalendarEventDto(
            EventId, "gid-1", "Team Meeting",
            FixedStart, FixedEnd,
            false, null, null,
            [calA, calB]);

        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [FixedDateKey] = [dto] }
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        result.Days[FixedDateKey].Should().AllSatisfy(v => v.AllCalendars.Should().HaveCount(2));
    }

    [Fact]
    public async Task GetEventsForMonthAsync_NoDtoTypeInViewModels()
    {
        // Regression: AllCalendars must be IReadOnlyList<CalendarSummaryViewModel>, not EventCalendarDto
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Event",
            FixedStart, FixedEnd,
            false, null, null,
            [calA]);

        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [FixedDateKey] = [dto] }
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var vm = result.Days[FixedDateKey].Single();
        vm.AllCalendars.Should().AllBeOfType<CalendarSummaryViewModel>();
    }

    [Fact]
    public async Task GetEventsForMonthAsync_SingleCalendarEvent_ProducesOneViewModel()
    {
        // Arrange — event in 1 calendar → exactly 1 ViewModel
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Solo Event",
            FixedStart, FixedEnd,
            false, null, null,
            [calA]);

        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [FixedDateKey] = [dto] }
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        result.Days[FixedDateKey].Should().HaveCount(1);
    }

    [Fact]
    public async Task GetEventsForMonthAsync_MapsEventFieldsCorrectly()
    {
        // Arrange
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var start = new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);
        var today = "2026-03-21";
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Doctor Appointment",
            start, end,
            false, "123 Main St", "Annual checkup",
            [calA]);

        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [today] = [dto] }
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        var vm = result.Days[today].Single();
        vm.Id.Should().Be(EventId);
        vm.Title.Should().Be("Doctor Appointment");
        vm.Start.Should().Be(start);
        vm.End.Should().Be(end);
        vm.IsAllDay.Should().BeFalse();
        vm.Location.Should().Be("123 Main St");
        vm.Description.Should().Be("Annual checkup");
        vm.CalendarInfoId.Should().Be(CalAId);
        vm.CalendarDisplayName.Should().Be("Cal A");
        vm.CalendarColor.Should().Be("#ff0000");
    }

    [Fact]
    public async Task GetEventsForMonthAsync_EmptyDays_ReturnsEmptyMonthViewModel()
    {
        // Arrange
        var monthViewDto = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new()
        };

        var sut = CreateSut(monthViewDto);

        // Act
        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        // Assert
        result.Days.Should().BeEmpty();
    }

    private static CalendarApiService CreateSut(MonthViewDto responseBody)
    {
        var json = JsonSerializer.Serialize(responseBody);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        };

        return new CalendarApiService(httpClient);
    }
}
