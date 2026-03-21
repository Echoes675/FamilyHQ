using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using Xunit;

namespace FamilyHQ.Core.Tests.Validators;

public class UpdateEventRequestValidatorTests
{
    private static UpdateEventRequest Valid() =>
        new("Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_Title_Fails()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(Valid() with { Title = "" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Title_Over200Chars_Fails()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(
            Valid() with { Title = new string('x', 201) });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task End_Before_Start_Fails()
    {
        var now = DateTimeOffset.UtcNow;
        var result = await new UpdateEventRequestValidator().ValidateAsync(
            new UpdateEventRequest("Title", now.AddHours(1), now, false, null, null));
        result.IsValid.Should().BeFalse();
    }
}
