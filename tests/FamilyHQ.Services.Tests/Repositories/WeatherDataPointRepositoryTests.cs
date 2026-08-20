namespace FamilyHQ.Services.Tests.Repositories;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

public class WeatherDataPointRepositoryTests
{
    private readonly FakeFamilyHqDbContext _db = new();
    private readonly FakeTimeProvider _fakeTime;

    public WeatherDataPointRepositoryTests()
    {
        // Fixed clock: 2026-06-18T12:00Z (well within UTC June 18)
        _fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));
    }

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

    private WeatherDataPoint MakeCurrent(int locationId) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = _fakeTime.GetUtcNow(),
            DataType = WeatherDataType.Current,
            RetrievedAt = _fakeTime.GetUtcNow(),
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 18,
            WindSpeedKmh = 7,
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
        _db.Setup<WeatherDataPoint>([p, q]);

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
        _db.Setup<WeatherDataPoint>([r, s]);

        var result = await CreateSut().GetDailyAsync(1, days: 7, ianaTimeZone: null);

        result.Should().ContainSingle("only S falls in UTC June 18 or later");
        result[0].Timestamp.Should().Be(s.Timestamp);
    }

    [Fact]
    public async Task GetDailyAsync_NullZone_StartBoundIsUtcMidnightInclusive()
    {
        // FHQ-118 regression pin: the null-zone window must start at EXACTLY the UTC midnight of the
        // TimeProvider's "today" (2026-06-18T00:00:00Z), inclusive. A derivation that bypasses the
        // TimeProvider (DateTimeOffset.UtcNow.Date) or anchors to host-local midnight shifts this instant.
        // T (exactly 2026-06-18T00:00:00Z): first instant of the window — INCLUDED.
        // U (2026-06-17T23:59:59Z): one second before the window — EXCLUDED.
        var t = MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        var u = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 23, 59, 59, TimeSpan.Zero));
        _db.Setup<WeatherDataPoint>([t, u]);

        var result = await CreateSut().GetDailyAsync(1, days: 7, ianaTimeZone: null);

        result.Should().ContainSingle("the start bound is inclusive at exactly UTC midnight of today");
        result[0].Timestamp.Should().Be(t.Timestamp);
    }

    [Fact]
    public async Task GetDailyAsync_NullZone_EndBoundIsExclusiveAfterRequestedDays()
    {
        // Fake clock: 2026-06-18T12:00Z. days: 2 → window [2026-06-18T00:00Z, 2026-06-20T00:00Z).
        // V (2026-06-19T23:59:59Z): last second of day 2 — INCLUDED.
        // W (exactly 2026-06-20T00:00:00Z): end bound — EXCLUDED (half-open window).
        var v = MakeDaily(1, new DateTimeOffset(2026, 6, 19, 23, 59, 59, TimeSpan.Zero));
        var w = MakeDaily(1, new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));
        _db.Setup<WeatherDataPoint>([v, w]);

        var result = await CreateSut().GetDailyAsync(1, days: 2, ianaTimeZone: null);

        result.Should().ContainSingle("the end bound is exclusive at UTC midnight after the requested days");
        result[0].Timestamp.Should().Be(v.Timestamp);
    }

    [Fact]
    public async Task GetDailyAsync_WithIanaZone_StartBoundIsLocalMidnightInclusive()
    {
        // Dublin BST (UTC+1): local midnight June 18 = 2026-06-17T23:00:00Z exactly.
        // M (exactly 2026-06-17T23:00:00Z): first instant of the local day — INCLUDED.
        // N (2026-06-17T22:59:59Z): one second before local midnight — EXCLUDED.
        var m = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 23, 0, 0, TimeSpan.Zero));
        var n = MakeDaily(1, new DateTimeOffset(2026, 6, 17, 22, 59, 59, TimeSpan.Zero));
        _db.Setup<WeatherDataPoint>([m, n]);

        var result = await CreateSut().GetDailyAsync(1, days: 7, ianaTimeZone: "Europe/Dublin");

        result.Should().ContainSingle("the start bound is inclusive at exactly Dublin local midnight");
        result[0].Timestamp.Should().Be(m.Timestamp);
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
        _db.Setup<WeatherDataPoint>([a, b, c]);

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
        _db.Setup<WeatherDataPoint>([x, y]);

        var result = await CreateSut().GetHourlyAsync(1, new DateOnly(2026, 6, 18), ianaTimeZone: null);

        result.Should().ContainSingle();
        result[0].Timestamp.Should().Be(y.Timestamp);
    }

    // ── FHQ-159: which stored rows a refresh replaces ────────────────────────────────────────────
    //
    // ReplaceSectionsAsync is SectionsReplacedBy + RowsReplacedBy + "delete, insert, commit". The
    // first two ARE the retention rule: before FHQ-159 the delete matched on LocationSettingId
    // alone, so a response whose hourly block came back empty wiped the stored hourly rows as a
    // side effect of rewriting daily. These tests compose the two exactly as the repository does
    // and assert which stored rows a given incoming payload removes. ExecuteDeleteAsync needs a
    // real provider, so the statement itself is not exercised here.

    private static List<WeatherDataPoint> RemovedBy(
        IEnumerable<WeatherDataPoint> stored, int locationSettingId, params WeatherDataPoint[] incoming)
    {
        var sections = WeatherDataPointRepository.SectionsReplacedBy([.. incoming]);
        var predicate = WeatherDataPointRepository.RowsReplacedBy(locationSettingId, sections).Compile();
        return stored.Where(predicate).ToList();
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingOnlyDaily_LeavesStoredHourlyIntact()
    {
        var storedHourly = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([storedHourly, storedDaily], 1,
            MakeDaily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().ContainSingle("only the daily section was carried").Which
            .Should().BeSameAs(storedDaily);
        removed.Should().NotContain(storedHourly,
            "an empty incoming hourly section must not wipe the stored hourly rows");
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingOnlyHourly_LeavesStoredDailyAndCurrentIntact()
    {
        var storedHourly = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        var storedCurrent = MakeCurrent(1);

        var removed = RemovedBy([storedHourly, storedDaily, storedCurrent], 1,
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)));

        removed.Should().Equal(storedHourly);
    }

    [Fact]
    public void ReplacedRows_RefreshWithoutACurrentBlock_LeavesTheStoredCurrentReadingIntact()
    {
        // Pairs with BuildDataPoints writing no Current row for an absent current block: the
        // previous reading must survive the refresh so it can stand for its retention window.
        var storedCurrent = MakeCurrent(1);
        var storedDaily = MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([storedCurrent, storedDaily], 1,
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)),
            MakeDaily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().NotContain(storedCurrent);
        removed.Should().Equal(storedDaily);
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingEverySection_ReplacesEverySection()
    {
        var storedHourly = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        var storedCurrent = MakeCurrent(1);

        var removed = RemovedBy([storedHourly, storedDaily, storedCurrent], 1,
            MakeCurrent(1),
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)),
            MakeDaily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().HaveCount(3, "a full response still replaces the location's data wholesale");
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingNothing_RemovesNothing()
    {
        // The whole-response failure mode: a payload with no rows at all must leave every stored
        // section standing rather than blanking the location.
        var stored = new List<WeatherDataPoint>
        {
            MakeCurrent(1),
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero)),
            MakeDaily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero))
        };

        RemovedBy(stored, 1).Should().BeEmpty();
    }

    [Fact]
    public void ReplacedRows_NeverTouchesAnotherLocation()
    {
        var mine = MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var theirs = MakeHourly(2, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([mine, theirs], 1,
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)));

        removed.Should().Equal(mine);
    }

    [Fact]
    public void SectionsReplacedBy_PayloadCarryingOneSectionTwice_NamesItOnce()
    {
        var sections = WeatherDataPointRepository.SectionsReplacedBy([
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero)),
            MakeHourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero))
        ]);

        sections.Should().ContainSingle(
            "the delete is one set-based statement, so the section list is a parameter, not a loop")
            .Which.Should().Be(WeatherDataType.Hourly);
    }
}
