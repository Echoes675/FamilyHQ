using FamilyHQ.Core.Models;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Core.Tests.Models;

public class SyncJobSourceExtensionsTests
{
    [Fact]
    public void IsReconcileOnly_IsTrue_ForDesignationChange()
        => SyncJobSource.DesignationChange.IsReconcileOnly().Should().BeTrue();

    [Theory]
    [InlineData(SyncJobSource.Webhook)]
    [InlineData(SyncJobSource.Periodic)]
    [InlineData(SyncJobSource.Login)]
    public void IsReconcileOnly_IsFalse_ForGoogleSyncSources(SyncJobSource source)
        => source.IsReconcileOnly().Should().BeFalse();
}
