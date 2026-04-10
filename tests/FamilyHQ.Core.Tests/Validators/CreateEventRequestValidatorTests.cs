using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using Xunit;

namespace FamilyHQ.Core.Tests.Validators;

public class CreateEventRequestValidatorTests
{
    private static readonly Guid CalA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static CreateEventRequest Valid(IReadOnlyList<Guid>? calIds = null) =>
        new(calIds ?? [CalA], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

    [Fact]
    public async Task Valid_SingleCalendar_Passes()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA]));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_MultipleCalendars_Passes()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA, CalB]));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Null_CalendarInfoIds_Fails()
    {
        var request = Valid() with { MemberCalendarInfoIds = null! };
        var result = await new CreateEventRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_CalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_CalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA, CalA]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyGuid_InCalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([Guid.Empty]));
        result.IsValid.Should().BeFalse();
    }
}
