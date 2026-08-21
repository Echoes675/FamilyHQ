namespace FamilyHQ.Core.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Weather;
using FluentAssertions;

/// <summary>
/// FHQ-159: which stored rows a refresh replaces.
/// <para>
/// Before FHQ-159 the delete matched on <c>LocationSettingId</c> alone, so a response whose hourly
/// block came back empty wiped the stored hourly rows as a side effect of rewriting daily. These
/// tests pin the rule itself. That the repository actually applies it — that
/// <c>ReplaceSectionsAsync</c> issues this predicate and not a wider one against real PostgreSQL —
/// is pinned by the "A weather update carrying no hourly data leaves the stored hourly forecast in
/// place" E2E scenario in <c>Weather.feature</c>; <c>ExecuteDeleteAsync</c> needs a real provider,
/// so no unit test can cover that half.
/// </para>
/// </summary>
public class WeatherRetentionTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static WeatherDataPoint Hourly(int locationId, DateTimeOffset timestamp) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = timestamp,
            DataType = WeatherDataType.Hourly,
            RetrievedAt = RetrievedAt,
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 15,
            WindSpeedKmh = 5,
            IsWindy = false
        };

    private static WeatherDataPoint Current(int locationId) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = RetrievedAt,
            DataType = WeatherDataType.Current,
            RetrievedAt = RetrievedAt,
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 18,
            WindSpeedKmh = 7,
            IsWindy = false
        };

    private static WeatherDataPoint Daily(int locationId, DateTimeOffset timestamp) =>
        new()
        {
            LocationSettingId = locationId,
            Timestamp = timestamp,
            DataType = WeatherDataType.Daily,
            RetrievedAt = RetrievedAt,
            Condition = WeatherCondition.Clear,
            TemperatureCelsius = 20,
            WindSpeedKmh = 10,
            IsWindy = false,
            HighCelsius = 25,
            LowCelsius = 15
        };

    /// <summary>Composes the two rules exactly as the repository's delete does.</summary>
    private static List<WeatherDataPoint> RemovedBy(
        IEnumerable<WeatherDataPoint> stored, int locationSettingId, params WeatherDataPoint[] incoming)
    {
        var sections = WeatherRetention.SectionsReplacedBy([.. incoming]);
        var predicate = WeatherRetention.RowsReplacedBy(locationSettingId, sections).Compile();
        return stored.Where(predicate).ToList();
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingOnlyDaily_LeavesStoredHourlyIntact()
    {
        var storedHourly = Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = Daily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([storedHourly, storedDaily], 1,
            Daily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().ContainSingle("only the daily section was carried").Which
            .Should().BeSameAs(storedDaily);
        removed.Should().NotContain(storedHourly,
            "an empty incoming hourly section must not wipe the stored hourly rows");
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingOnlyHourly_LeavesStoredDailyAndCurrentIntact()
    {
        var storedHourly = Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = Daily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        var storedCurrent = Current(1);

        var removed = RemovedBy([storedHourly, storedDaily, storedCurrent], 1,
            Hourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)));

        removed.Should().Equal(storedHourly);
    }

    [Fact]
    public void ReplacedRows_RefreshWithoutACurrentBlock_LeavesTheStoredCurrentReadingIntact()
    {
        // Pairs with BuildDataPoints writing no Current row for an absent current block: the
        // previous reading must survive the refresh so it can stand for its retention window.
        var storedCurrent = Current(1);
        var storedDaily = Daily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([storedCurrent, storedDaily], 1,
            Hourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)),
            Daily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().NotContain(storedCurrent);
        removed.Should().Equal(storedDaily);
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingEverySection_ReplacesEverySection()
    {
        var storedHourly = Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var storedDaily = Daily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        var storedCurrent = Current(1);

        var removed = RemovedBy([storedHourly, storedDaily, storedCurrent], 1,
            Current(1),
            Hourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)),
            Daily(1, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero)));

        removed.Should().HaveCount(3, "a full response still replaces the location's data wholesale");
    }

    [Fact]
    public void ReplacedRows_RefreshCarryingNothing_RemovesNothing()
    {
        // The whole-response failure mode: a payload with no rows at all must leave every stored
        // section standing rather than blanking the location.
        var stored = new List<WeatherDataPoint>
        {
            Current(1),
            Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero)),
            Daily(1, new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero))
        };

        RemovedBy(stored, 1).Should().BeEmpty();
    }

    [Fact]
    public void ReplacedRows_NeverTouchesAnotherLocation()
    {
        var mine = Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));
        var theirs = Hourly(2, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero));

        var removed = RemovedBy([mine, theirs], 1,
            Hourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero)));

        removed.Should().Equal(mine);
    }

    [Fact]
    public void SectionsReplacedBy_PayloadCarryingOneSectionTwice_NamesItOnce()
    {
        var sections = WeatherRetention.SectionsReplacedBy([
            Hourly(1, new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero)),
            Hourly(1, new DateTimeOffset(2026, 6, 18, 10, 0, 0, TimeSpan.Zero))
        ]);

        sections.Should().ContainSingle(
            "the delete is one set-based statement, so the section list is a parameter, not a loop")
            .Which.Should().Be(WeatherDataType.Hourly);
    }
}
