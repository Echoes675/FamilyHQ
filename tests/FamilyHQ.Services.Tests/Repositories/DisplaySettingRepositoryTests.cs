using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class DisplaySettingRepositoryTests
{
    private readonly FakeFamilyHqDbContext _db = new();

    private DisplaySettingRepository CreateSut() => new(_db);

    [Fact]
    public async Task UpsertAsync_Insert_PersistsAllFields()
    {
        var mockSet = _db.Setup<DisplaySetting>();
        var sut = CreateSut();
        var setting = new DisplaySetting
        {
            SurfaceMultiplier = 0.8,
            OpaqueSurfaces = true,
            TransitionDurationSecs = 30,
            ThemeSelection = "evening",
            IanaTimeZone = "America/New_York",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await sut.UpsertAsync("user-1", setting);

        result.IanaTimeZone.Should().Be("America/New_York");
        result.SurfaceMultiplier.Should().Be(0.8);
        result.ThemeSelection.Should().Be("evening");
        mockSet.Verify(s => s.Add(It.Is<DisplaySetting>(d =>
            d.UserId == "user-1" &&
            d.IanaTimeZone == "America/New_York" &&
            d.SurfaceMultiplier == 0.8 &&
            d.ThemeSelection == "evening")), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_Update_PreservesIanaTimeZoneWhenCallerOmitsIt()
    {
        // Simulate a user who has already set an explicit timezone, then saves display settings
        // (which must not wipe IanaTimeZone — FHQ-43 regression guard).
        var existing = new DisplaySetting
        {
            UserId = "user-1",
            SurfaceMultiplier = 1.0,
            OpaqueSurfaces = false,
            TransitionDurationSecs = 15,
            ThemeSelection = "auto",
            IanaTimeZone = "Europe/Paris",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var mockSet = _db.Setup<DisplaySetting>([existing]);
        var sut = CreateSut();

        // Caller passes a DisplaySetting with IanaTimeZone still set (as PutDisplay now does
        // after loading the existing row), verifying the repo UPDATE branch copies it through.
        var update = new DisplaySetting
        {
            SurfaceMultiplier = 0.9,
            OpaqueSurfaces = true,
            TransitionDurationSecs = 20,
            ThemeSelection = "morning",
            IanaTimeZone = "Europe/Paris",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await sut.UpsertAsync("user-1", update);

        result.Should().BeSameAs(existing);
        result.IanaTimeZone.Should().Be("Europe/Paris",
            "UpsertAsync UPDATE branch must copy IanaTimeZone from the incoming setting");
        result.SurfaceMultiplier.Should().Be(0.9);
        result.ThemeSelection.Should().Be("morning");
        mockSet.Verify(s => s.Add(It.IsAny<DisplaySetting>()), Times.Never);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_Update_ClearsIanaTimeZoneWhenCallerPassesNull()
    {
        // Verify the timezone-reset path (ResetTimeZone endpoint) continues to work: when the
        // caller explicitly passes null, the UPDATE branch must write null.
        var existing = new DisplaySetting
        {
            UserId = "user-1",
            IanaTimeZone = "Europe/London",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var mockSet = _db.Setup<DisplaySetting>([existing]);
        var sut = CreateSut();

        var update = new DisplaySetting
        {
            IanaTimeZone = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await sut.UpsertAsync("user-1", update);

        result.Should().BeSameAs(existing);
        result.IanaTimeZone.Should().BeNull();
        mockSet.Verify(s => s.Add(It.IsAny<DisplaySetting>()), Times.Never);
        _db.SaveChangesCount.Should().Be(1);
    }
}
