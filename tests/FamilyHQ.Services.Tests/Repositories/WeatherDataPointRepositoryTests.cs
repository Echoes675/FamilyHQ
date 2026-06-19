namespace FamilyHQ.Services.Tests.Repositories;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

public class WeatherDataPointRepositoryTests : IDisposable
{
    private readonly FamilyHqDbContext _db;
    private readonly FakeTimeProvider _fakeTime;

    public WeatherDataPointRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new FamilyHqDbContext(options);
        // Fixed clock: 2026-06-18T12:00Z (well within UTC June 18)
        _fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose() => _db.Dispose();

    private WeatherDataPointRepository CreateSut() => new(_db, _fakeTime);

    private WeatherDataPoint MakeHourly(int locationId, DateTimeOffset timestamp) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = timestamp,
            DataType = WeatherDataType.Hourly,
            RetrievedAt = _fakeTime.GetUtcNow(),
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 15,
            WindSpeedKmh = 5,
            IsWindy = false
        };

    private WeatherDataPoint MakeDaily(int locationId, DateTimeOffset timestamp) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = timestamp,
            DataType = WeatherDataType.Daily,
            RetrievedAt = _fakeTime.GetUtcNow(),
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 20,
            WindSpeedKmh = 10,
            IsWindy = false,
            HighCelsius = 25,
            LowCelsius = 15
        };

    [Fact]
    public async Task GetDailyAsync_WithIanaZone_ExcludesYesterdayLocalData()
    {
        // Fake clock: 2026-06-18T12:00Z. Dublin BST (UTC+1): local today = June 18.
        // Local midnight June 18 BST = 2026-06-17T23:00Z.
        // P (UTC 22:30 June 17): BST 23:30 June 17 — yesterday local — EXCLUDED.
        // Q (UTC 23:10 June 17): BST 00:10 June 18 — today local    — INCLUDED.
        var p = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 22, 30, 0, TimeSpan.Zero));
        var q = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 23, 10, 0, TimeSpan.Zero));
        _db.WeatherDataPoints.AddRange(p, q);
        await _db.SaveChangesAsync();

        var result = await CreateSut().GetDailyAsync(1, days: 7, ianaTimeZone: "Europe/Dublin");

        result.Should().ContainSingle("only Q falls in BST today-or-later window");
        result[0].Timestamp.Should().Be(q.Timestamp);
    }

    [Fact]
    public async Task GetDailyAsync_NullZone_UsesUtcTodayAnchor()
    {
        // Fake clock: 2026-06-18T12:00Z. Null zone: UTC today = June 18 [00:00Z, ∞).
        // R (UTC 22:30 June 17): before UTC midnight June 18 — EXCLUDED.
        // S (UTC 00:30 June 18): after UTC midnight June 18  — INCLUDED.
        var r = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 22, 30, 0, TimeSpan.Zero));
        var s = MakeDaily(1, new DateTimeOffset(2026, 6, 18,  0, 30, 0, TimeSpan.Zero));
        _db.WeatherDataPoints.AddRange(r, s);
        await _db.SaveChangesAsync();

        var result = await CreateSut().GetDailyAsync(1, days: 7, ianaTimeZone: null);

        result.Should().ContainSingle("only S falls in UTC June 18 or later");
        result[0].Timestamp.Should().Be(s.Timestamp);
    }

    [Fact]
    public async Task GetHourlyAsync_WithIanaZone_ReturnsDataInLocalDayWindow()
    {
        // Dublin BST (UTC+1): local June 18 spans UTC 2026-06-17T23:00 → 2026-06-18T23:00.
        // A: UTC 2026-06-17T23:30 = BST June 18 00:30 — INSIDE local June 18 window.
        // B: UTC 2026-06-18T22:30 = BST June 18 23:30 — INSIDE local June 18 window.
        // C: UTC 2026-06-18T23:30 = BST June 19 00:30 — OUTSIDE local June 18 window.
        var a = MakeHourly(1, new DateTimeOffset(2026, 6, 17, 23, 30, 0, TimeSpan.Zero));
        var b = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 22, 30, 0, TimeSpan.Zero));
        var c = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 23, 30, 0, TimeSpan.Zero));
        _db.WeatherDataPoints.AddRange(a, b, c);
        await _db.SaveChangesAsync();

        var result = await CreateSut().GetHourlyAsync(1, new DateOnly(2026, 6, 18), "Europe/Dublin");

        result.Should().HaveCount(2, "A and B are in the BST June 18 window; C is June 19 BST");
        result.Should().Contain(x => x.Timestamp == a.Timestamp);
        result.Should().Contain(x => x.Timestamp == b.Timestamp);
        result.Should().NotContain(x => x.Timestamp == c.Timestamp);
    }

    [Fact]
    public async Task GetHourlyAsync_NullZone_UsesUtcMidnightBounds()
    {
        // Null zone falls back to UTC midnight. June 18 UTC = [00:00Z, 00:00Z next day).
        // X: UTC 2026-06-17T23:30 — OUTSIDE UTC June 18 window (is June 17 UTC).
        // Y: UTC 2026-06-18T12:00 — INSIDE UTC June 18 window.
        var x = MakeHourly(1, new DateTimeOffset(2026, 6, 17, 23, 30, 0, TimeSpan.Zero));
        var y = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 12,  0, 0, TimeSpan.Zero));
        _db.WeatherDataPoints.AddRange(x, y);
        await _db.SaveChangesAsync();

        var result = await CreateSut().GetHourlyAsync(1, new DateOnly(2026, 6, 18), ianaTimeZone: null);

        result.Should().ContainSingle();
        result[0].Timestamp.Should().Be(y.Timestamp);
    }
}
