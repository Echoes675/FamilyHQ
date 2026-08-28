using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class DayThemeRepositoryTests
{
    private const string KioskA = "kiosk-a";
    private const string KioskB = "kiosk-b";

    private readonly FakeFamilyHqDbContext _db = new();

    private DayThemeRepository CreateSut() => new(_db);

    [Fact]
    public async Task UpsertAsync_Insert_PersistsIanaTimeZone()
    {
        var mockSet = _db.Setup<DayTheme>();
        var sut = CreateSut();
        var dayTheme = new DayTheme
        {
            UserId = KioskA,
            Date = new DateOnly(2024, 6, 21),
            MorningStart = new TimeOnly(5, 30),
            DaytimeStart = new TimeOnly(6, 0),
            EveningStart = new TimeOnly(20, 0),
            NightStart = new TimeOnly(21, 30),
            IanaTimeZone = "Europe/Dublin"
        };

        var result = await sut.UpsertAsync(dayTheme);

        result.IanaTimeZone.Should().Be("Europe/Dublin");
        mockSet.Verify(s => s.Add(It.Is<DayTheme>(d => d.IanaTimeZone == "Europe/Dublin")), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_Update_CopiesIanaTimeZoneFromIncomingRecord()
    {
        // FHQ-160: a same-day cross-timezone move recalculates today's theme. Today's row already
        // exists (startup created it), so the UPDATE branch runs. It must carry the new zone across
        // with the new boundaries — otherwise the new-zone sunrise/sunset times are stored against
        // the OLD zone, and both GetNextBoundaryDelay and DeriveCurrentPeriod read them in the
        // wrong zone (wrong wake instant, wrong period).
        var existing = new DayTheme
        {
            UserId = KioskA,
            Date = new DateOnly(2024, 6, 21),
            MorningStart = new TimeOnly(5, 30),
            DaytimeStart = new TimeOnly(6, 0),
            EveningStart = new TimeOnly(20, 0),
            NightStart = new TimeOnly(21, 30),
            IanaTimeZone = "Europe/Dublin"
        };
        var mockSet = _db.Setup<DayTheme>([existing]);
        var sut = CreateSut();

        // The service always upserts a freshly-constructed (detached) DayTheme, so the UPDATE branch
        // is the only thing that can carry the recalculated zone onto the stored row.
        var recalculated = new DayTheme
        {
            UserId = KioskA,
            Date = new DateOnly(2024, 6, 21),
            MorningStart = new TimeOnly(4, 15),
            DaytimeStart = new TimeOnly(5, 45),
            EveningStart = new TimeOnly(19, 30),
            NightStart = new TimeOnly(21, 0),
            IanaTimeZone = "America/New_York"
        };

        var result = await sut.UpsertAsync(recalculated);

        result.Should().BeSameAs(existing);
        result.IanaTimeZone.Should().Be("America/New_York",
            "the UPDATE branch must copy IanaTimeZone, or the new zone's boundaries are stored against the old zone");
        result.MorningStart.Should().Be(new TimeOnly(4, 15));
        result.DaytimeStart.Should().Be(new TimeOnly(5, 45));
        result.EveningStart.Should().Be(new TimeOnly(19, 30));
        result.NightStart.Should().Be(new TimeOnly(21, 0));
        mockSet.Verify(s => s.Add(It.IsAny<DayTheme>()), Times.Never);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task GetByDateAsync_ReturnsOnlyTheRequestedKiosksRow()
    {
        // FHQ-177: two kiosks, same date, different places. Before scoping there was one global row
        // per date, so whichever kiosk wrote last decided everyone's sunrise. Matching on Date alone
        // here would hand kiosk A's boundaries to kiosk B.
        var date = new DateOnly(2024, 6, 21);
        _db.Setup<DayTheme>(
        [
            new DayTheme { UserId = KioskA, Date = date, IanaTimeZone = "Europe/Dublin", NightStart = new TimeOnly(23, 0) },
            new DayTheme { UserId = KioskB, Date = date, IanaTimeZone = "Australia/Sydney", NightStart = new TimeOnly(18, 30) }
        ]);

        var result = await CreateSut().GetByDateAsync(KioskB, date);

        result.Should().NotBeNull();
        result!.IanaTimeZone.Should().Be("Australia/Sydney");
        result.NightStart.Should().Be(new TimeOnly(18, 30));
    }

    [Fact]
    public async Task GetByDateAsync_ReturnsNull_WhenOnlyAnotherKioskHasARowForThatDate()
    {
        var date = new DateOnly(2024, 6, 21);
        _db.Setup<DayTheme>([new DayTheme { UserId = KioskA, Date = date, IanaTimeZone = "Europe/Dublin" }]);

        var result = await CreateSut().GetByDateAsync(KioskB, date);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_Insert_WhenTheSameDateBelongsToAnotherKiosk()
    {
        // The upsert resolves the existing row through GetByDateAsync, so it inherits the scoping.
        // If it did not, kiosk B's first calculation would overwrite kiosk A's row instead of
        // creating its own.
        var date = new DateOnly(2024, 6, 21);
        var mockSet = _db.Setup<DayTheme>([new DayTheme { UserId = KioskA, Date = date, IanaTimeZone = "Europe/Dublin" }]);

        await CreateSut().UpsertAsync(new DayTheme { UserId = KioskB, Date = date, IanaTimeZone = "Australia/Sydney" });

        mockSet.Verify(s => s.Add(It.Is<DayTheme>(d => d.UserId == KioskB)), Times.Once);
    }
}
