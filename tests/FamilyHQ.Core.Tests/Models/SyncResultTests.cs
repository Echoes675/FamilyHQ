using FamilyHQ.Core.Models;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Core.Tests.Models;

public class SyncResultTests
{
    [Fact]
    public void HadChanges_IsFalse_WhenChangedCountIsZero()
        => new SyncResult(0).HadChanges.Should().BeFalse();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public void HadChanges_IsTrue_WhenChangedCountIsPositive(int count)
        => new SyncResult(count).HadChanges.Should().BeTrue();
}
