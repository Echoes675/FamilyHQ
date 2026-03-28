using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests;

public class UserPreferencesServiceTests
{
    private static FamilyHqDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FamilyHqDbContext(options);
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsDefault_WhenNoPreferencesExist()
    {
        using var context = CreateInMemoryContext();
        var sut = new UserPreferencesService(context);
        
        var result = await sut.GetPreferencesAsync("user123");
        
        Assert.Equal(2, result.EventDensity); // Default density
        Assert.Null(result.CalendarColumnOrder);
    }

    [Fact]
    public async Task SavePreferencesAsync_CreatesNewRecord_WhenNoneExists()
    {
        using var context = CreateInMemoryContext();
        var sut = new UserPreferencesService(context);
        
        var prefs = new UserPreferences { EventDensity = 3 };
        await sut.SavePreferencesAsync("user123", prefs);
        
        var saved = await context.UserPreferences.FirstOrDefaultAsync(x => x.UserId == "user123");
        Assert.NotNull(saved);
        Assert.Equal(3, saved.EventDensity);
    }

    [Fact]
    public async Task SavePreferencesAsync_UpdatesExistingRecord()
    {
        using var context = CreateInMemoryContext();
        var sut = new UserPreferencesService(context);
        
        // Create initial
        await sut.SavePreferencesAsync("user123", new UserPreferences { EventDensity = 1 });
        
        // Update
        await sut.SavePreferencesAsync("user123", new UserPreferences { EventDensity = 3 });
        
        var count = await context.UserPreferences.CountAsync(x => x.UserId == "user123");
        Assert.Equal(1, count); // Should not create duplicate
        
        var saved = await context.UserPreferences.FirstAsync(x => x.UserId == "user123");
        Assert.Equal(3, saved.EventDensity);
    }
}
