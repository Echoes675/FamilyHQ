using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Xunit;

// Deliberately in the test-root namespace: a "FamilyHQ.WebApi.Tests.Options" namespace would
// shadow Microsoft.Extensions.Options.Options for every sibling test namespace's
// unqualified Options.Create(...) calls.
namespace FamilyHQ.WebApi.Tests;

public class JwtSessionOptionsTests
{
    [Fact]
    public void Validate_WithDefaults_DoesNotThrowAndCapIs730Days()
    {
        // Arrange
        var options = new JwtSessionOptions();

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
        options.MaxSessionAgeDays.Should().Be(730);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenMaxSessionAgeNotPositive_ThrowsInvalidOperationException(double days)
    {
        // Arrange
        var options = new JwtSessionOptions { MaxSessionAgeDays = days };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxSessionAgeDays*");
    }
}
