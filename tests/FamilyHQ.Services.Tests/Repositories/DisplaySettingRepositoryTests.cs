using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class DisplaySettingRepositoryTests : IDisposable
{
    private readonly FamilyHqDbContext _db;

    public DisplaySettingRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new FamilyHqDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private DisplaySettingRepository CreateSut() => new(_db);

    [Fact]
    public async Task UpsertAsync_Insert_PersistsAllFields()
    {
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
    }

    [Fact]
    public async Task UpsertAsync_Update_PreservesIanaTimeZoneWhenCallerOmitsIt()
    {
        // Simulate a user who has already set an explicit timezone, then saves display settings
        // (which must not wipe IanaTimeZone — FHQ-43 regression guard).
        var sut = CreateSut();
        _db.DisplaySettings.Add(new DisplaySetting
        {
            UserId = "user-1",
            SurfaceMultiplier = 1.0,
            OpaqueSurfaces = false,
            TransitionDurationSecs = 15,
            ThemeSelection = "auto",
            IanaTimeZone = "Europe/Paris",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

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

        result.IanaTimeZone.Should().Be("Europe/Paris",
            "UpsertAsync UPDATE branch must copy IanaTimeZone from the incoming setting");
        result.SurfaceMultiplier.Should().Be(0.9);
        result.ThemeSelection.Should().Be("morning");
    }

    [Fact]
    public async Task UpsertAsync_Update_ClearsIanaTimeZoneWhenCallerPassesNull()
    {
        // Verify the timezone-reset path (ResetTimeZone endpoint) continues to work: when the
        // caller explicitly passes null, the UPDATE branch must write null.
        var sut = CreateSut();
        _db.DisplaySettings.Add(new DisplaySetting
        {
            UserId = "user-1",
            IanaTimeZone = "Europe/London",
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

        var update = new DisplaySetting
        {
            IanaTimeZone = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var result = await sut.UpsertAsync("user-1", update);

        result.IanaTimeZone.Should().BeNull();
    }
}
