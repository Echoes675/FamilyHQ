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

    [Fact]
    public async Task GetConnectionStatusAsync_ParsesNeedsReauthResponse()
    {
        // Backend serialises an anonymous object → camelCase keys.
        const string json = """
        {
          "status": "needs_reauth",
          "lastError": "Token has been expired or revoked.",
          "since": "2026-05-13T18:34:00+00:00"
        }
        """;

        var sut = CreateSutWithRawResponse(HttpStatusCode.OK, json);

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be("needs_reauth");
        result.LastError.Should().Be("Token has been expired or revoked.");
        result.Since.Should().Be(new DateTimeOffset(2026, 5, 13, 18, 34, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetConnectionStatusAsync_ParsesActiveResponse()
    {
        const string json = """{"status":"active","lastError":null,"since":null}""";

        var sut = CreateSutWithRawResponse(HttpStatusCode.OK, json);

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be("active");
        result.LastError.Should().BeNull();
        result.Since.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatusAsync_OnUnauthorized_ReturnsNull()
    {
        var sut = CreateSutWithRawResponse(HttpStatusCode.Unauthorized, "");

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatusAsync_OnNetworkFailure_ReturnsNull()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var sut = new CalendarApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEventsForMonthAsync_RecurringDto_FlowsRecurrenceIntoViewModel()
    {
        var calA = new EventCalendarDto(CalAId, "Cal A", "#ff0000");
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Standup",
            FixedStart, FixedEnd,
            false, null, null,
            [calA],
            IsRecurring: true,
            RecurrenceRule: "RRULE:FREQ=WEEKLY;BYDAY=MO");

        var monthViewDto = new MonthViewDto { Year = 2026, Month = 3, Days = new() { [FixedDateKey] = [dto] } };
        var sut = CreateSut(monthViewDto);

        var result = await sut.GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        var vm = result.Days[FixedDateKey].Single();
        vm.IsRecurring.Should().BeTrue();
        vm.RecurrenceRule.Should().Be("RRULE:FREQ=WEEKLY;BYDAY=MO");
    }

    [Fact]
    public async Task UpdateRecurringEventAsync_PutsToScopedRecurringEndpoint()
    {
        var (sut, requests) = CreateCapturingSut(SerializeEventDto());

        await sut.UpdateRecurringEventAsync(
            EventId,
            new UpdateEventRequest("New", FixedStart, FixedEnd, false, null, null, RecurrenceRule: "RRULE:FREQ=DAILY"),
            RecurrenceScope.ThisAndFollowing,
            CancellationToken.None);

        var req = requests.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.PathAndQuery.Should().Contain($"api/events/{EventId}/recurring");
        req.RequestUri!.Query.Should().Contain("scope=ThisAndFollowing");
    }

    [Fact]
    public async Task DeleteRecurringEventAsync_DeletesToScopedRecurringEndpoint()
    {
        var (sut, requests) = CreateCapturingSut("");

        await sut.DeleteRecurringEventAsync(EventId, RecurrenceScope.AllInSeries, CancellationToken.None);

        var req = requests.Should().ContainSingle().Subject;
        req.Method.Should().Be(HttpMethod.Delete);
        req.RequestUri!.PathAndQuery.Should().Contain($"api/events/{EventId}/recurring");
        req.RequestUri!.Query.Should().Contain("scope=AllInSeries");
    }

    // ---------------------------------------------------------------------------------------------
    // FHQ-175: every non-success response passes through ONE seam that reads the body and parses it
    // defensively as ProblemDetails. EnsureSuccessStatusCode() used to discard the body.
    // ---------------------------------------------------------------------------------------------

    private const string ReauthProblemBody = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
          "title": "Reconnect Google Calendar",
          "status": 409,
          "detail": "Your Google connection needs to be re-authorised.",
          "code": "needs_reauth",
          "source": "calendar_api",
          "reconnectUrl": "/api/auth/login",
          "userMessage": "Your Google connection needs to be re-authorised."
        }
        """;

    /// <summary>
    /// Every method that throws on failure, so the seam is proven on all eleven former
    /// EnsureSuccessStatusCode() sites — reverting any one of them back fails its row here.
    /// </summary>
    public static IEnumerable<object[]> ThrowingCalls() =>
    [
        ["UpdateCalendarSettingsAsync", (Func<CalendarApiService, Task>)(s => s.UpdateCalendarSettingsAsync(CalAId, true, false))],
        ["SaveCalendarOrderAsync",      (Func<CalendarApiService, Task>)(s => s.SaveCalendarOrderAsync(new() { [CalAId] = 0 }))],
        ["GetEventsForMonthAsync",      (Func<CalendarApiService, Task>)(s => s.GetEventsForMonthAsync(2026, 3))],
        ["CreateEventAsync",            (Func<CalendarApiService, Task>)(s => s.CreateEventAsync(NewCreateRequest()))],
        ["UpdateEventAsync",            (Func<CalendarApiService, Task>)(s => s.UpdateEventAsync(EventId, NewUpdateRequest()))],
        ["DeleteEventAsync",            (Func<CalendarApiService, Task>)(s => s.DeleteEventAsync(EventId))],
        ["UpdateRecurringEventAsync",   (Func<CalendarApiService, Task>)(s => s.UpdateRecurringEventAsync(EventId, NewUpdateRequest(), RecurrenceScope.AllInSeries))],
        ["DeleteRecurringEventAsync",   (Func<CalendarApiService, Task>)(s => s.DeleteRecurringEventAsync(EventId, RecurrenceScope.ThisOnly))],
        ["SetEventMembersAsync",        (Func<CalendarApiService, Task>)(s => s.SetEventMembersAsync(EventId, [CalAId]))],
        ["TriggerSyncAsync",            (Func<CalendarApiService, Task>)(s => s.TriggerSyncAsync())],
        ["RegisterWebhooksAsync",       (Func<CalendarApiService, Task>)(s => s.RegisterWebhooksAsync())]
    ];

    [Theory]
    [MemberData(nameof(ThrowingCalls))]
    public async Task EveryThrowingCall_OnProblemDetailsFailure_ThrowsCalendarApiExceptionCarryingTheBody(
        string _, Func<CalendarApiService, Task> call)
    {
        var sut = CreateSutWithRawResponse(HttpStatusCode.Conflict, ReauthProblemBody);

        var act = () => call(sut);

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ex.Title.Should().Be("Reconnect Google Calendar");
        ex.UserMessage.Should().Be("Your Google connection needs to be re-authorised.");
        ex.Code.Should().Be("needs_reauth");
    }

    [Fact]
    public async Task CreateEventAsync_OnProblemDetailsWithoutUserMessage_LeavesUserMessageNull()
    {
        // A non-opted-in domain failure: title and detail present, no userMessage extension. The
        // kiosk must fall back to its generic string — Detail is never promoted to UserMessage.
        const string body = """
            {"title":"Validation Failed","status":400,"detail":"CalendarInfoId 00000000-0000-0000-0000-000000000001 is not known to the user."}
            """;
        var sut = CreateSutWithRawResponse(HttpStatusCode.BadRequest, body);

        var act = () => sut.CreateEventAsync(NewCreateRequest());

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Title.Should().Be("Validation Failed");
        ex.UserMessage.Should().BeNull();
        ex.Code.Should().BeNull();
        ex.Message.Should().NotContain("00000000-0000-0000-0000-000000000001", "Detail never reaches the exception");
    }

    [Fact]
    public async Task CreateEventAsync_OnFluentValidationArrayBody_FallsBackCleanly()
    {
        // EventsController returns BadRequest(validation.Errors) — a raw FluentValidation array that
        // bypasses DomainExceptionHandler. Root is an array, not an object: nothing is recognised.
        const string body = """
            [{"propertyName":"Title","errorMessage":"'Title' must be 200 characters or fewer.","attemptedValue":"xxxxxxxxxx","severity":0,"errorCode":"MaximumLengthValidator"}]
            """;
        var sut = CreateSutWithRawResponse(HttpStatusCode.BadRequest, body);

        var act = () => sut.CreateEventAsync(NewCreateRequest());

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.Title.Should().BeNull();
        ex.UserMessage.Should().BeNull();
        ex.Code.Should().BeNull();
        ex.Message.Should().NotContainAny(["attemptedValue", "MaximumLengthValidator", "xxxxxxxxxx"]);
    }

    [Fact]
    public async Task SetEventMembersAsync_OnBareJsonStringBody_FallsBackCleanly()
    {
        // EventsController returns BadRequest("At least one member is required.") — a bare JSON
        // string. Root is a string, not an object: nothing is recognised.
        const string body = "\"At least one member is required.\"";
        var sut = CreateSutWithRawResponse(HttpStatusCode.BadRequest, body);

        var act = () => sut.SetEventMembersAsync(EventId, [CalAId]);

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ex.UserMessage.Should().BeNull();
        ex.Title.Should().BeNull();
    }

    [Fact]
    public async Task DeleteEventAsync_OnEmptyBody_FallsBackCleanly()
    {
        var sut = CreateSutWithRawResponse(HttpStatusCode.InternalServerError, "");

        var act = () => sut.DeleteEventAsync(EventId);

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.UserMessage.Should().BeNull();
        ex.Title.Should().BeNull();
        ex.Code.Should().BeNull();
    }

    [Fact]
    public async Task UpdateEventAsync_OnMalformedJsonBody_FallsBackCleanly()
    {
        // A proxy's HTML error page, or a truncated body: not JSON at all. Must not throw a
        // JsonException out of the seam and must not surface the raw text.
        const string body = "<html><body><h1>502 Bad Gateway</h1><p>nginx-internal-SECRET</p></body></html>";
        var sut = CreateSutWithRawResponse(HttpStatusCode.BadGateway, body);

        var act = () => sut.UpdateEventAsync(EventId, NewUpdateRequest());

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        ex.UserMessage.Should().BeNull();
        ex.Message.Should().NotContain("SECRET");
    }

    [Fact]
    public async Task CreateEventAsync_OnUserMessageOfWrongJsonType_LeavesUserMessageNull()
    {
        // Only a string is a message. A number/object/null under the same key is a shape mismatch.
        const string body = """{"title":"Odd","status":400,"userMessage":{"text":"nested"},"code":42}""";
        var sut = CreateSutWithRawResponse(HttpStatusCode.BadRequest, body);

        var act = () => sut.CreateEventAsync(NewCreateRequest());

        var ex = (await act.Should().ThrowAsync<CalendarApiException>()).Which;
        ex.Title.Should().Be("Odd");
        ex.UserMessage.Should().BeNull();
        ex.Code.Should().BeNull();
    }

    [Fact]
    public async Task CreateEventAsync_OnFailure_NoLongerThrowsHttpRequestException()
    {
        // The whole point: EnsureSuccessStatusCode() threw HttpRequestException and lost the body.
        var sut = CreateSutWithRawResponse(HttpStatusCode.Conflict, ReauthProblemBody);

        var act = () => sut.CreateEventAsync(NewCreateRequest());

        await act.Should().NotThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CreateEventAsync_OnSuccess_DoesNotThrow()
    {
        var (sut, _) = CreateCapturingSut(SerializeEventDto());

        var act = () => sut.CreateEventAsync(NewCreateRequest());

        await act.Should().NotThrowAsync();
    }

    private static CreateEventRequest NewCreateRequest() =>
        new([CalAId], "Dentist", FixedStart, FixedEnd, false, null, null);

    private static UpdateEventRequest NewUpdateRequest() =>
        new("Dentist", FixedStart, FixedEnd, false, null, null);

    private static string SerializeEventDto()
    {
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Std", FixedStart, FixedEnd, false, null, null,
            [new EventCalendarDto(CalAId, "Cal A", "#ff0000")],
            IsRecurring: true, RecurrenceRule: "RRULE:FREQ=DAILY");
        return JsonSerializer.Serialize(dto);
    }

    private static (CalendarApiService Sut, List<HttpRequestMessage> Requests) CreateCapturingSut(string body)
    {
        var requests = new List<HttpRequestMessage>();
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => requests.Add(r))
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://test.local/") };
        return (new CalendarApiService(httpClient), requests);
    }

    private static CalendarApiService CreateSut(MonthViewDto responseBody)
    {
        var json = JsonSerializer.Serialize(responseBody);
        return CreateSutWithRawResponse(HttpStatusCode.OK, json);
    }

    private static CalendarApiService CreateSutWithRawResponse(HttpStatusCode status, string body)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        };

        return new CalendarApiService(httpClient);
    }
}
