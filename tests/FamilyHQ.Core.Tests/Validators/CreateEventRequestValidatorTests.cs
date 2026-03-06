using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Core.Tests.Validators;

public class CreateEventRequestValidatorTests
{
    private readonly CreateEventRequestValidator _validator = new();

    [Fact]
    public void Validator_WhenValidRequest_ShouldNotHaveErrors()
    {
        // Arrange
        var request = new CreateEventRequest(
            CalendarInfoId: Guid.NewGuid(),
            Title: "Test Event",
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: "Home",
            Description: "A descriptive event"
        );

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_WhenEmptyTitle_ShouldHaveError()
    {
        // Arrange
        var request = new CreateEventRequest(
            CalendarInfoId: Guid.NewGuid(),
            Title: "",
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            IsAllDay: false,
            Location: null,
            Description: null
        );

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validator_WhenEndIsBeforeStart_ShouldHaveError()
    {
        // Arrange
        var request = new CreateEventRequest(
            CalendarInfoId: Guid.NewGuid(),
            Title: "Test Event",
            Start: DateTimeOffset.UtcNow.AddHours(1),
            End: DateTimeOffset.UtcNow,
            IsAllDay: false,
            Location: null,
            Description: null
        );

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "End" && e.ErrorMessage.Contains("after start time"));
    }
}
