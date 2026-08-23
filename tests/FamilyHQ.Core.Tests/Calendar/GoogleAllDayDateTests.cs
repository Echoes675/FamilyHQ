using FamilyHQ.Core.Calendar;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Calendar;

/// <summary>
/// FHQ-174 — the shared conversion between Google's all-day <c>date</c> and the instant FamilyHQ
/// stores.
/// <para>
/// <b>What these tests can and cannot do.</b> They document the contract. They do NOT catch a
/// reintroduction of the host-offset bug: CI runs at a zero host offset, where
/// <c>DateTimeOffset.Parse("2026-06-15")</c> returns exactly what
/// <see cref="GoogleAllDayDate.Parse"/> returns, so every assertion below passes on the defect. The
/// build-breaking mechanisms are the <c>BannedApiAnalyzers</c> rule in
/// <c>build/BannedSymbols.txt</c> and <see cref="DateOnlyParseGuardTests"/>, which requires every
/// <c>DateTime</c>/<c>DateTimeOffset</c> parse to pass <c>DateTimeStyles.AssumeUniversal</c>.
/// </para>
/// </summary>
public class GoogleAllDayDateTests
{
    [Theory]
    [InlineData("2026-06-15", 2026, 6, 15)]
    // The two EU DST transition dates of 2026. They are here as data points, NOT as a discriminator:
    // what a zone-anchored parse yields depends on the host's offset at MIDNIGHT on the date, and
    // Europe/Dublin is still at +00:00 at 00:00 on 29 March (the change happens at 01:00 UTC), so
    // that case is identical under the defect even on a Dublin host. No case in this file catches the
    // defect on CI, where the host offset is zero — the guards do.
    [InlineData("2026-03-29", 2026, 3, 29)]
    [InlineData("2026-10-25", 2026, 10, 25)]
    // Leap day, and the first and last days of a year — the boundaries an off-by-one lands on.
    [InlineData("2028-02-29", 2028, 2, 29)]
    [InlineData("2026-01-01", 2026, 1, 1)]
    [InlineData("2026-12-31", 2026, 12, 31)]
    public void Parse_RfcFullDate_ReturnsMidnightUtcOnThatDay(string value, int year, int month, int day)
    {
        var parsed = GoogleAllDayDate.Parse(value);

        parsed.Offset.Should().Be(TimeSpan.Zero);
        parsed.UtcDateTime.Should().Be(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_RfcFullDate_RoundTripsBackToTheSameString()
    {
        // The property the whole fix rests on: what Google sent is what FamilyHQ can send back.
        const string date = "2026-06-15";

        GoogleAllDayDate.Parse(date).ToString(GoogleAllDayDate.DateFormat).Should().Be(date);
    }

    [Theory]
    // Locale-shaped, which a lenient parse would happily accept as a different day.
    [InlineData("15/06/2026")]
    [InlineData("06/15/2026")]
    // A timed value arriving on the date field.
    [InlineData("2026-06-15T00:00:00Z")]
    // Shapes RFC 3339 does not define for a full-date.
    [InlineData("2026-6-15")]
    [InlineData("20260615")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ValueThatIsNotAnRfcFullDate_ThrowsFormatException(string value) =>
        FluentActions.Invoking(() => GoogleAllDayDate.Parse(value)).Should().Throw<FormatException>();

    [Theory]
    [InlineData("15/06/2026")]
    [InlineData("2026-06-15T00:00:00Z")]
    [InlineData("2026-6-15")]
    [InlineData("")]
    public void TryParse_ValueThatIsNotAnRfcFullDate_ReturnsFalse(string value) =>
        // The sync loop's variant: one unreadable item must cost that item, not the page it arrived
        // on and not every retry after it.
        GoogleAllDayDate.TryParse(value, out _).Should().BeFalse();

    [Fact]
    public void TryParse_RfcFullDate_ReturnsTheSameInstantAsParse()
    {
        GoogleAllDayDate.TryParse("2026-06-15", out var parsed).Should().BeTrue();

        parsed.Should().Be(GoogleAllDayDate.Parse("2026-06-15"));
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => GoogleAllDayDate.Parse(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Parse_ExceptionMessage_DoesNotEchoTheOffendingValue() =>
        // The date field is calendar content. A malformed one still says nothing about the family's
        // day in a log line (see the logging standard); the length is enough to diagnose the shape.
        FluentActions.Invoking(() => GoogleAllDayDate.Parse("15/06/2026"))
            .Should().Throw<FormatException>()
            .Which.Message.Should().NotContain("15/06/2026");

    [Fact]
    public void AtMidnightUtc_DateWithATimeComponent_DiscardsTheTime()
    {
        var instant = GoogleAllDayDate.AtMidnightUtc(new DateTime(2026, 6, 15, 9, 30, 0));

        instant.Offset.Should().Be(TimeSpan.Zero);
        instant.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void AtMidnightUtc_AnyDateTimeKind_ReturnsTheSameInstant(DateTimeKind kind)
    {
        // A picked calendar date carries no zone whatever its Kind says. Passing a Local-kind value
        // would make the DateTimeOffset constructor throw on any host that is not at UTC, which is
        // precisely the host-dependence this type exists to remove.
        var instant = GoogleAllDayDate.AtMidnightUtc(DateTime.SpecifyKind(new DateTime(2026, 6, 15), kind));

        instant.Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void AtMidnightUtc_AndParse_AgreeOnTheSameDay() =>
        // The kiosk and the sync must store an all-day boundary identically, or the database holds
        // two representations of the same thing and only one of them survives a write back.
        GoogleAllDayDate.AtMidnightUtc(new DateTime(2026, 6, 15))
            .Should().Be(GoogleAllDayDate.Parse("2026-06-15"));

    [Theory]
    [InlineData("2026-06-15T00:00:00Z", true)]
    // The stored shape of the defect: a UTC+1 host's midnight.
    [InlineData("2026-06-14T23:00:00Z", false)]
    [InlineData("2026-06-15T08:00:00Z", false)]
    public void IsMidnightUtc_ReturnsWhetherTheBoundaryIsCanonical(string iso, bool expected) =>
        GoogleAllDayDate.IsMidnightUtc(
                DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind))
            .Should().Be(expected);

    [Fact]
    public void IsMidnightUtc_MidnightInANonZeroOffset_IsFalse() =>
        // The audit asks about the INSTANT, not the wall clock: 2026-06-15T00:00+01:00 is the
        // previous day in UTC, and it is the previous day that the outbound formatting would send.
        GoogleAllDayDate.IsMidnightUtc(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.FromHours(1)))
            .Should().BeFalse();

    [Fact]
    public void IsInclusiveEndOfDay_TheLegacyEndOfDayTick_IsTrue() =>
        // The SECOND legacy shape, and the reason the audit reports it apart from the day shift:
        // this row is not evidence of a host-offset problem, and counting it as one would corrupt
        // the number the existing-data decision rests on.
        GoogleAllDayDate.IsInclusiveEndOfDay(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero).AddDays(1).AddTicks(-1))
            .Should().BeTrue();

    [Theory]
    // Google's exclusive next-day midnight — the canonical shape.
    [InlineData("2026-06-16T00:00:00Z")]
    // The day-shift signature: a UTC+1 host's midnight. Not the inclusive-end shape.
    [InlineData("2026-06-15T23:00:00Z")]
    public void IsInclusiveEndOfDay_AnythingElse_IsFalse(string iso) =>
        GoogleAllDayDate.IsInclusiveEndOfDay(
                DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind))
            .Should().BeFalse();

    [Fact]
    public void IsInclusiveEndOfDayAndIsMidnightUtc_AreMutuallyExclusive()
    {
        // The audit relies on this: it recognises the inclusive-end shape FIRST and excludes it from
        // the day-shift count, which is only sound if no value can be both.
        var inclusiveEnd = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero).AddTicks(-1);

        GoogleAllDayDate.IsInclusiveEndOfDay(inclusiveEnd).Should().BeTrue();
        GoogleAllDayDate.IsMidnightUtc(inclusiveEnd).Should().BeFalse();
    }
}
