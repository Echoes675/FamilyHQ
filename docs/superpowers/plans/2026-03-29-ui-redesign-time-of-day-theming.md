# UI Redesign & Time-of-Day Theming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the plain Bootstrap look of FamilyHQ with a warm ambient design system that shifts through four time-of-day colour themes (Morning, Daytime, Evening, Night), driven by real sunrise/sunset data for the user's location, with 45-second CSS transitions and a new Settings page for location configuration.

**Architecture:** A new `DayTheme` DB entity stores daily sunrise/sunset boundaries; `DayThemeSchedulerService` (BackgroundService) fires `ThemeChanged` via SignalR at each boundary. The Blazor WASM client derives the correct theme on load from `GET /api/daytheme/today` and updates on SignalR push. All colour is driven by CSS custom properties registered via `@property`; the 45-second bleed transition is pure CSS with no JS involvement after the `data-theme` attribute is set.

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core / PostgreSQL, SunCalcNet (NuGet), SignalR, CSS `@property`, ip-api.com (IP geolocation), Nominatim OSM (geocoding), xUnit / Moq / FluentAssertions.

---

## File Map

### New files — Backend

| Path | Responsibility |
|---|---|
| `src/FamilyHQ.Core/Models/DayTheme.cs` | `DayTheme` entity |
| `src/FamilyHQ.Core/Models/LocationSetting.cs` | `LocationSetting` entity |
| `src/FamilyHQ.Core/Models/TimeOfDayPeriod.cs` | `TimeOfDayPeriod` enum |
| `src/FamilyHQ.Core/Models/DayThemeBoundaries.cs` | Value record returned by SunCalculatorService |
| `src/FamilyHQ.Core/Interfaces/IDayThemeRepository.cs` | Repository interface |
| `src/FamilyHQ.Core/Interfaces/ILocationSettingRepository.cs` | Repository interface |
| `src/FamilyHQ.Core/Interfaces/ISunCalculatorService.cs` | Sun calc interface |
| `src/FamilyHQ.Core/Interfaces/ILocationService.cs` | Effective location interface |
| `src/FamilyHQ.Core/Interfaces/IGeocodingService.cs` | Geocoding interface |
| `src/FamilyHQ.Core/Interfaces/IDayThemeService.cs` | Day theme service interface |
| `src/FamilyHQ.Core/Interfaces/IDayThemeScheduler.cs` | Scheduler trigger interface |
| `src/FamilyHQ.Core/DTOs/DayThemeDto.cs` | API response DTO |
| `src/FamilyHQ.Core/DTOs/LocationSettingDto.cs` | API response DTO |
| `src/FamilyHQ.Core/DTOs/SaveLocationRequest.cs` | API request DTO |
| `src/FamilyHQ.Core/DTOs/LocationResult.cs` | Internal location result |
| `src/FamilyHQ.Data/Configurations/DayThemeConfiguration.cs` | EF config |
| `src/FamilyHQ.Data/Configurations/LocationSettingConfiguration.cs` | EF config |
| `src/FamilyHQ.Data/Repositories/DayThemeRepository.cs` | Repository impl |
| `src/FamilyHQ.Data/Repositories/LocationSettingRepository.cs` | Repository impl |
| `src/FamilyHQ.Services/Theme/SunCalculatorService.cs` | SunCalcNet wrapper |
| `src/FamilyHQ.Services/Theme/LocationService.cs` | IP fallback + DB location |
| `src/FamilyHQ.Services/Theme/GeocodingService.cs` | Nominatim geocoding |
| `src/FamilyHQ.Services/Theme/DayThemeService.cs` | Ensure/recalc/get today |
| `src/FamilyHQ.Services/Theme/DayThemeSchedulerService.cs` | BackgroundService + IDayThemeScheduler |
| `src/FamilyHQ.WebApi/Controllers/DayThemeController.cs` | `GET /api/daytheme/today` |
| `src/FamilyHQ.WebApi/Controllers/SettingsController.cs` | `GET/POST /api/settings/location` |

### Modified files — Backend

| Path | Change |
|---|---|
| `src/FamilyHQ.Data/FamilyHqDbContext.cs` | Add `DbSet<DayTheme>`, `DbSet<LocationSetting>` |
| `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs` | Register new repositories |
| `src/FamilyHQ.Services/ServiceCollectionExtensions.cs` | Register new services + BackgroundService |
| `src/FamilyHQ.WebApi/Program.cs` | Register HttpClients for LocationService + GeocodingService |

### New migration

| Path | Change |
|---|---|
| `src/FamilyHQ.Data.PostgreSQL/Migrations/` | `AddDayThemeAndLocationSetting` migration |

### New files — Frontend

| Path | Responsibility |
|---|---|
| `src/FamilyHQ.WebUi/wwwroot/js/theme.js` | `setTheme(period)` JS interop helper |
| `src/FamilyHQ.WebUi/Services/IThemeService.cs` | ThemeService interface |
| `src/FamilyHQ.WebUi/Services/ThemeService.cs` | Init from API + SignalR subscription |
| `src/FamilyHQ.WebUi/Services/ISettingsApiService.cs` | Settings API client interface |
| `src/FamilyHQ.WebUi/Services/SettingsApiService.cs` | Typed HTTP client for settings endpoints |
| `src/FamilyHQ.WebUi/Pages/Settings.razor` | Settings page `/settings` |

### Modified files — Frontend

| Path | Change |
|---|---|
| `src/FamilyHQ.WebUi/wwwroot/css/app.css` | Full CSS theme system (replace all colours) |
| `src/FamilyHQ.WebUi/wwwroot/index.html` | Add `theme-bg`/`weather-overlay` divs, import `theme.js` |
| `src/FamilyHQ.WebUi/Services/SignalRService.cs` | Add `OnThemeChanged` event |
| `src/FamilyHQ.WebUi/Layout/MainLayout.razor` | Inject + init `IThemeService` |
| `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor` | Remove user info, add settings gear link |
| `src/FamilyHQ.WebUi/Program.cs` | Register `IThemeService`, `ISettingsApiService` |

### Test files

| Path | Responsibility |
|---|---|
| `tests/FamilyHQ.Services.Tests/Theme/SunCalculatorServiceTests.cs` | Unit tests |
| `tests/FamilyHQ.Services.Tests/Theme/DayThemeServiceTests.cs` | Unit tests |
| `tests/FamilyHQ.Services.Tests/Theme/LocationServiceTests.cs` | Unit tests |
| `tests/FamilyHQ.WebApi.Tests/Controllers/DayThemeControllerTests.cs` | Controller tests |
| `tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs` | Controller tests |

---

## Task 1: Core Types — Enums, DTOs, Interfaces

**Files:**
- Create: `src/FamilyHQ.Core/Models/TimeOfDayPeriod.cs`
- Create: `src/FamilyHQ.Core/Models/DayThemeBoundaries.cs`
- Create: `src/FamilyHQ.Core/DTOs/DayThemeDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/LocationSettingDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/SaveLocationRequest.cs`
- Create: `src/FamilyHQ.Core/DTOs/LocationResult.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IDayThemeRepository.cs`
- Create: `src/FamilyHQ.Core/Interfaces/ILocationSettingRepository.cs`
- Create: `src/FamilyHQ.Core/Interfaces/ISunCalculatorService.cs`
- Create: `src/FamilyHQ.Core/Interfaces/ILocationService.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IGeocodingService.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IDayThemeService.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IDayThemeScheduler.cs`

- [ ] **Step 1: Create TimeOfDayPeriod enum**

```csharp
// src/FamilyHQ.Core/Models/TimeOfDayPeriod.cs
namespace FamilyHQ.Core.Models;

public enum TimeOfDayPeriod
{
    Morning,
    Daytime,
    Evening,
    Night
}
```

- [ ] **Step 2: Create DayThemeBoundaries record**

```csharp
// src/FamilyHQ.Core/Models/DayThemeBoundaries.cs
namespace FamilyHQ.Core.Models;

public record DayThemeBoundaries(
    TimeOnly MorningStart,
    TimeOnly DaytimeStart,
    TimeOnly EveningStart,
    TimeOnly NightStart
);
```

- [ ] **Step 3: Create DTOs**

```csharp
// src/FamilyHQ.Core/DTOs/DayThemeDto.cs
namespace FamilyHQ.Core.DTOs;

public record DayThemeDto(
    DateOnly Date,
    TimeOnly MorningStart,
    TimeOnly DaytimeStart,
    TimeOnly EveningStart,
    TimeOnly NightStart,
    string CurrentPeriod
);
```

```csharp
// src/FamilyHQ.Core/DTOs/LocationSettingDto.cs
namespace FamilyHQ.Core.DTOs;

public record LocationSettingDto(
    string PlaceName,
    bool IsAutoDetected
);
```

```csharp
// src/FamilyHQ.Core/DTOs/SaveLocationRequest.cs
namespace FamilyHQ.Core.DTOs;

public record SaveLocationRequest(string PlaceName);
```

```csharp
// src/FamilyHQ.Core/DTOs/LocationResult.cs
namespace FamilyHQ.Core.DTOs;

public record LocationResult(
    string PlaceName,
    double Latitude,
    double Longitude,
    bool IsAutoDetected
);
```

- [ ] **Step 4: Create repository interfaces**

```csharp
// src/FamilyHQ.Core/Interfaces/IDayThemeRepository.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeRepository
{
    Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default);
}
```

```csharp
// src/FamilyHQ.Core/Interfaces/ILocationSettingRepository.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationSettingRepository
{
    Task<LocationSetting?> GetAsync(CancellationToken ct = default);
    Task<LocationSetting> UpsertAsync(LocationSetting locationSetting, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create service interfaces**

```csharp
// src/FamilyHQ.Core/Interfaces/ISunCalculatorService.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ISunCalculatorService
{
    DayThemeBoundaries CalculateBoundaries(double latitude, double longitude, DateOnly date);
}
```

```csharp
// src/FamilyHQ.Core/Interfaces/ILocationService.cs
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationService
{
    Task<LocationResult> GetEffectiveLocationAsync(CancellationToken ct = default);
}
```

```csharp
// src/FamilyHQ.Core/Interfaces/IGeocodingService.cs
namespace FamilyHQ.Core.Interfaces;

public interface IGeocodingService
{
    Task<(double Latitude, double Longitude)> GeocodeAsync(string placeName, CancellationToken ct = default);
}
```

```csharp
// src/FamilyHQ.Core/Interfaces/IDayThemeService.cs
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeService
{
    Task EnsureTodayAsync(CancellationToken ct = default);
    Task RecalculateForTodayAsync(CancellationToken ct = default);
    Task<DayThemeDto> GetTodayAsync(CancellationToken ct = default);
}
```

```csharp
// src/FamilyHQ.Core/Interfaces/IDayThemeScheduler.cs
namespace FamilyHQ.Core.Interfaces;

public interface IDayThemeScheduler
{
    Task TriggerRecalculationAsync();
}
```

- [ ] **Step 6: Build Core project to verify no errors**

```bash
dotnet build src/FamilyHQ.Core/FamilyHQ.Core.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.Core/
git commit -m "feat(theme): add core types, DTOs, and service interfaces for time-of-day theming"
```

---

## Task 2: Data Models, EF Configurations, Repositories, Migration

**Files:**
- Create: `src/FamilyHQ.Core/Models/DayTheme.cs`
- Create: `src/FamilyHQ.Core/Models/LocationSetting.cs`
- Create: `src/FamilyHQ.Data/Configurations/DayThemeConfiguration.cs`
- Create: `src/FamilyHQ.Data/Configurations/LocationSettingConfiguration.cs`
- Create: `src/FamilyHQ.Data/Repositories/DayThemeRepository.cs`
- Create: `src/FamilyHQ.Data/Repositories/LocationSettingRepository.cs`
- Modify: `src/FamilyHQ.Data/FamilyHqDbContext.cs`
- Modify: `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create entity models**

```csharp
// src/FamilyHQ.Core/Models/DayTheme.cs
namespace FamilyHQ.Core.Models;

public class DayTheme
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly MorningStart { get; set; }
    public TimeOnly DaytimeStart { get; set; }
    public TimeOnly EveningStart { get; set; }
    public TimeOnly NightStart { get; set; }
}
```

```csharp
// src/FamilyHQ.Core/Models/LocationSetting.cs
namespace FamilyHQ.Core.Models;

public class LocationSetting
{
    public int Id { get; set; }
    public string PlaceName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Create EF configurations**

```csharp
// src/FamilyHQ.Data/Configurations/DayThemeConfiguration.cs
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DayThemeConfiguration : IEntityTypeConfiguration<DayTheme>
{
    public void Configure(EntityTypeBuilder<DayTheme> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).IsRequired();
        builder.HasIndex(x => x.Date).IsUnique();
        builder.Property(x => x.MorningStart).IsRequired();
        builder.Property(x => x.DaytimeStart).IsRequired();
        builder.Property(x => x.EveningStart).IsRequired();
        builder.Property(x => x.NightStart).IsRequired();
    }
}
```

```csharp
// src/FamilyHQ.Data/Configurations/LocationSettingConfiguration.cs
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class LocationSettingConfiguration : IEntityTypeConfiguration<LocationSetting>
{
    public void Configure(EntityTypeBuilder<LocationSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlaceName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
```

- [ ] **Step 3: Add DbSets to DbContext**

In `src/FamilyHQ.Data/FamilyHqDbContext.cs`, add two DbSet properties after the existing ones:

```csharp
public DbSet<DayTheme> DayThemes => Set<DayTheme>();
public DbSet<LocationSetting> LocationSettings => Set<LocationSetting>();
```

- [ ] **Step 4: Create repository implementations**

```csharp
// src/FamilyHQ.Data/Repositories/DayThemeRepository.cs
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DayThemeRepository : IDayThemeRepository
{
    private readonly FamilyHqDbContext _context;

    public DayThemeRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => await _context.DayThemes.FirstOrDefaultAsync(x => x.Date == date, ct);

    public async Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default)
    {
        var existing = await GetByDateAsync(dayTheme.Date, ct);
        if (existing is null)
        {
            _context.DayThemes.Add(dayTheme);
            await _context.SaveChangesAsync(ct);
            return dayTheme;
        }

        existing.MorningStart = dayTheme.MorningStart;
        existing.DaytimeStart = dayTheme.DaytimeStart;
        existing.EveningStart = dayTheme.EveningStart;
        existing.NightStart = dayTheme.NightStart;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
```

```csharp
// src/FamilyHQ.Data/Repositories/LocationSettingRepository.cs
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class LocationSettingRepository : ILocationSettingRepository
{
    private readonly FamilyHqDbContext _context;

    public LocationSettingRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<LocationSetting?> GetAsync(CancellationToken ct = default)
        => await _context.LocationSettings.FirstOrDefaultAsync(ct);

    public async Task<LocationSetting> UpsertAsync(LocationSetting locationSetting, CancellationToken ct = default)
    {
        var existing = await GetAsync(ct);
        if (existing is null)
        {
            _context.LocationSettings.Add(locationSetting);
            await _context.SaveChangesAsync(ct);
            return locationSetting;
        }

        existing.PlaceName = locationSetting.PlaceName;
        existing.Latitude = locationSetting.Latitude;
        existing.Longitude = locationSetting.Longitude;
        existing.UpdatedAt = locationSetting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
```

- [ ] **Step 5: Register repositories**

In `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs`, add after the existing repository registrations:

```csharp
services.AddScoped<IDayThemeRepository, DayThemeRepository>();
services.AddScoped<ILocationSettingRepository, LocationSettingRepository>();
```

Add the required usings at the top if not already present:
```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Data.Repositories;
```

- [ ] **Step 6: Build Data projects to verify no errors**

```bash
dotnet build src/FamilyHQ.Data/FamilyHQ.Data.csproj && dotnet build src/FamilyHQ.Data.PostgreSQL/FamilyHQ.Data.PostgreSQL.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Create EF migration**

> **REQUIRES APPROVAL** — modifies database schema.

```bash
dotnet ef migrations add AddDayThemeAndLocationSetting --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```
Expected: Migration files created in `src/FamilyHQ.Data.PostgreSQL/Migrations/`.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyHQ.Core/Models/ src/FamilyHQ.Data/ src/FamilyHQ.Data.PostgreSQL/
git commit -m "feat(theme): add DayTheme and LocationSetting entities, repositories, and migration"
```

---

## Task 3: SunCalculatorService

**Files:**
- Create: `src/FamilyHQ.Services/Theme/SunCalculatorService.cs`
- Create: `tests/FamilyHQ.Services.Tests/Theme/SunCalculatorServiceTests.cs`
- Modify: `src/FamilyHQ.Services/FamilyHQ.Services.csproj` (add SunCalcNet NuGet)

> **REQUIRES APPROVAL** — installs a new NuGet package.

```bash
dotnet add src/FamilyHQ.Services/FamilyHQ.Services.csproj package SunCalcNet
```

- [ ] **Step 1: Write failing tests**

```csharp
// tests/FamilyHQ.Services.Tests/Theme/SunCalculatorServiceTests.cs
using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class SunCalculatorServiceTests
{
    private readonly SunCalculatorService _sut = new();

    [Fact]
    public void CalculateBoundaries_ReturnsCorrectOrder_ForKnownLocation()
    {
        // Edinburgh, 2024-06-21 (summer solstice — all four periods expected)
        var result = _sut.CalculateBoundaries(55.9533, -3.1883, new DateOnly(2024, 6, 21));

        result.MorningStart.Should().BeBefore(result.DaytimeStart);
        result.DaytimeStart.Should().BeBefore(result.EveningStart);
        result.EveningStart.Should().BeBefore(result.NightStart);
    }

    [Fact]
    public void CalculateBoundaries_MorningStart_IsCivilDawn()
    {
        // Edinburgh summer — civil dawn expected well before 5am
        var result = _sut.CalculateBoundaries(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        result.MorningStart.Hour.Should().BeLessThan(5);
    }

    [Fact]
    public void CalculateBoundaries_EveningStart_IsOneHourBeforeSunset()
    {
        // Edinburgh summer — sunset roughly 21:30, so evening ~20:30
        var result = _sut.CalculateBoundaries(55.9533, -3.1883, new DateOnly(2024, 6, 21));
        result.EveningStart.Hour.Should().BeGreaterThanOrEqualTo(20);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~SunCalculatorServiceTests" --no-build 2>&1 | tail -5
```
Expected: Build error (SunCalculatorService not found).

- [ ] **Step 3: Implement SunCalculatorService**

```csharp
// src/FamilyHQ.Services/Theme/SunCalculatorService.cs
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using SunCalcNet;

namespace FamilyHQ.Services.Theme;

public class SunCalculatorService : ISunCalculatorService
{
    public DayThemeBoundaries CalculateBoundaries(double latitude, double longitude, DateOnly date)
    {
        var utcDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var sunTimes = SunCalc.GetSunPhases(utcDate, latitude, longitude);

        var civilDawn = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Dawn.Value).PhaseTime.ToLocalTime());
        var sunrise   = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Sunrise.Value).PhaseTime.ToLocalTime());
        var sunset    = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Sunset.Value).PhaseTime.ToLocalTime());
        var civilDusk = TimeOnly.FromDateTime(sunTimes.First(x => x.Name.Value == SunPhaseName.Dusk.Value).PhaseTime.ToLocalTime());

        var eveningStart = sunset.Add(TimeSpan.FromHours(-1));

        return new DayThemeBoundaries(civilDawn, sunrise, eveningStart, civilDusk);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~SunCalculatorServiceTests" -v normal
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Services/ tests/FamilyHQ.Services.Tests/Theme/SunCalculatorServiceTests.cs
git commit -m "feat(theme): add SunCalculatorService using SunCalcNet for sunrise/sunset boundary calculation"
```

---

## Task 4: LocationService and GeocodingService

**Files:**
- Create: `src/FamilyHQ.Services/Theme/LocationService.cs`
- Create: `src/FamilyHQ.Services/Theme/GeocodingService.cs`
- Create: `tests/FamilyHQ.Services.Tests/Theme/LocationServiceTests.cs`
- Modify: `src/FamilyHQ.WebApi/Program.cs`

- [ ] **Step 1: Write failing tests for LocationService**

```csharp
// tests/FamilyHQ.Services.Tests/Theme/LocationServiceTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class LocationServiceTests
{
    private readonly Mock<ILocationSettingRepository> _repoMock = new();
    private readonly Mock<HttpClient> _httpClientMock = new();

    [Fact]
    public async Task GetEffectiveLocationAsync_ReturnsSavedSetting_WhenPresent()
    {
        var saved = new LocationSetting
        {
            PlaceName = "Edinburgh, Scotland",
            Latitude = 55.9533,
            Longitude = -3.1883,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _repoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saved);

        var httpClient = new HttpClient(new SuccessHandler());
        var sut = new LocationService(_repoMock.Object, httpClient);

        var result = await sut.GetEffectiveLocationAsync();

        result.PlaceName.Should().Be("Edinburgh, Scotland");
        result.Latitude.Should().Be(55.9533);
        result.IsAutoDetected.Should().BeFalse();
    }

    [Fact]
    public async Task GetEffectiveLocationAsync_ReturnsAutoDetected_WhenNoSetting()
    {
        _repoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);

        var httpClient = new HttpClient(new FakeIpApiHandler());
        var sut = new LocationService(_repoMock.Object, httpClient);

        var result = await sut.GetEffectiveLocationAsync();

        result.IsAutoDetected.Should().BeTrue();
        result.Latitude.Should().NotBe(0);
    }

    private class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private class FakeIpApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = """{"status":"success","city":"London","regionName":"England","country":"United Kingdom","lat":51.5074,"lon":-0.1278}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~LocationServiceTests" --no-build 2>&1 | tail -5
```
Expected: Build error (LocationService not found).

- [ ] **Step 3: Implement LocationService**

```csharp
// src/FamilyHQ.Services/Theme/LocationService.cs
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class LocationService : ILocationService
{
    private readonly ILocationSettingRepository _repo;
    private readonly HttpClient _httpClient;

    public LocationService(ILocationSettingRepository repo, HttpClient httpClient)
    {
        _repo = repo;
        _httpClient = httpClient;
    }

    public async Task<LocationResult> GetEffectiveLocationAsync(CancellationToken ct = default)
    {
        var saved = await _repo.GetAsync(ct);
        if (saved is not null)
            return new LocationResult(saved.PlaceName, saved.Latitude, saved.Longitude, IsAutoDetected: false);

        var response = await _httpClient.GetFromJsonAsync<IpApiResponse>(
            "http://ip-api.com/json/?fields=status,city,regionName,country,lat,lon", ct)
            ?? throw new InvalidOperationException("IP geolocation returned null response.");

        var placeName = $"{response.City}, {response.RegionName}, {response.Country}";
        return new LocationResult(placeName, response.Lat, response.Lon, IsAutoDetected: true);
    }

    private record IpApiResponse(string Status, string City, string RegionName, string Country, double Lat, double Lon);
}
```

- [ ] **Step 4: Implement GeocodingService**

```csharp
// src/FamilyHQ.Services/Theme/GeocodingService.cs
using System.Net.Http.Json;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Theme;

public class GeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;

    public GeocodingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(double Latitude, double Longitude)> GeocodeAsync(string placeName, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(placeName);
        var url = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=1";

        var results = await _httpClient.GetFromJsonAsync<NominatimResult[]>(url, ct);
        if (results is null || results.Length == 0)
            throw new InvalidOperationException($"No geocoding results found for '{placeName}'.");

        return (double.Parse(results[0].Lat, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(results[0].Lon, System.Globalization.CultureInfo.InvariantCulture));
    }

    private record NominatimResult(string Lat, string Lon);
}
```

- [ ] **Step 5: Register HttpClients in WebApi Program.cs**

In `src/FamilyHQ.WebApi/Program.cs`, add before `builder.Build()`:

```csharp
builder.Services.AddHttpClient<ILocationService, LocationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<IGeocodingService, GeocodingService>(client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyHQ/1.0");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

Add the required usings:
```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Theme;
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~LocationServiceTests" -v normal
```
Expected: 2 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.Services/Theme/ tests/FamilyHQ.Services.Tests/Theme/LocationServiceTests.cs src/FamilyHQ.WebApi/Program.cs
git commit -m "feat(theme): add LocationService (IP geolocation fallback) and GeocodingService (Nominatim OSM)"
```

---

## Task 5: DayThemeService

**Files:**
- Create: `src/FamilyHQ.Services/Theme/DayThemeService.cs`
- Create: `tests/FamilyHQ.Services.Tests/Theme/DayThemeServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/FamilyHQ.Services.Tests/Theme/DayThemeServiceTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class DayThemeServiceTests
{
    private readonly Mock<IDayThemeRepository> _dayThemeRepoMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();
    private readonly Mock<ISunCalculatorService> _sunCalcMock = new();

    private DayThemeService CreateSut() =>
        new(_dayThemeRepoMock.Object, _locationServiceMock.Object, _sunCalcMock.Object);

    [Fact]
    public async Task EnsureTodayAsync_DoesNotRecalculate_WhenRecordExists()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _dayThemeRepoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme { Date = today });

        await CreateSut().EnsureTodayAsync();

        _sunCalcMock.Verify(x => x.CalculateBoundaries(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task EnsureTodayAsync_Calculates_WhenNoRecordExists()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _dayThemeRepoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        _locationServiceMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult("Test", 55.0, -3.0, false));
        _sunCalcMock.Setup(x => x.CalculateBoundaries(55.0, -3.0, today))
            .Returns(new DayThemeBoundaries(
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30)));
        _dayThemeRepoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme dt, CancellationToken _) => dt);

        await CreateSut().EnsureTodayAsync();

        _sunCalcMock.Verify(x => x.CalculateBoundaries(55.0, -3.0, today), Times.Once);
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsDaytimePeriod_WhenNowBetweenSunriseAndEvening()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.Now;
        var daytimeStart = TimeOnly.FromDateTime(now.AddHours(-1));
        var eveningStart = TimeOnly.FromDateTime(now.AddHours(1));

        _dayThemeRepoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme
            {
                Date = today,
                MorningStart = TimeOnly.FromDateTime(now.AddHours(-3)),
                DaytimeStart = daytimeStart,
                EveningStart = eveningStart,
                NightStart = TimeOnly.FromDateTime(now.AddHours(3))
            });

        var result = await CreateSut().GetTodayAsync();

        result.CurrentPeriod.Should().Be("Daytime");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~DayThemeServiceTests" --no-build 2>&1 | tail -5
```
Expected: Build error (DayThemeService not found).

- [ ] **Step 3: Implement DayThemeService**

```csharp
// src/FamilyHQ.Services/Theme/DayThemeService.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Services.Theme;

public class DayThemeService : IDayThemeService
{
    private readonly IDayThemeRepository _dayThemeRepo;
    private readonly ILocationService _locationService;
    private readonly ISunCalculatorService _sunCalculator;

    public DayThemeService(
        IDayThemeRepository dayThemeRepo,
        ILocationService locationService,
        ISunCalculatorService sunCalculator)
    {
        _dayThemeRepo = dayThemeRepo;
        _locationService = locationService;
        _sunCalculator = sunCalculator;
    }

    public async Task EnsureTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existing = await _dayThemeRepo.GetByDateAsync(today, ct);
        if (existing is not null) return;

        await CalculateAndPersistAsync(today, ct);
    }

    public async Task RecalculateForTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await CalculateAndPersistAsync(today, ct);
    }

    public async Task<DayThemeDto> GetTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var record = await _dayThemeRepo.GetByDateAsync(today, ct)
            ?? throw new InvalidOperationException("No DayTheme record found for today.");

        var currentPeriod = DeriveCurrentPeriod(record);

        return new DayThemeDto(
            record.Date,
            record.MorningStart,
            record.DaytimeStart,
            record.EveningStart,
            record.NightStart,
            currentPeriod.ToString());
    }

    private async Task CalculateAndPersistAsync(DateOnly date, CancellationToken ct)
    {
        var location = await _locationService.GetEffectiveLocationAsync(ct);
        var boundaries = _sunCalculator.CalculateBoundaries(location.Latitude, location.Longitude, date);

        await _dayThemeRepo.UpsertAsync(new DayTheme
        {
            Date = date,
            MorningStart = boundaries.MorningStart,
            DaytimeStart = boundaries.DaytimeStart,
            EveningStart = boundaries.EveningStart,
            NightStart = boundaries.NightStart
        }, ct);
    }

    public static TimeOfDayPeriod DeriveCurrentPeriod(DayTheme record)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (now >= record.NightStart) return TimeOfDayPeriod.Night;
        if (now >= record.EveningStart) return TimeOfDayPeriod.Evening;
        if (now >= record.DaytimeStart) return TimeOfDayPeriod.Daytime;
        if (now >= record.MorningStart) return TimeOfDayPeriod.Morning;
        return TimeOfDayPeriod.Night;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/FamilyHQ.Services.Tests/ --filter "FullyQualifiedName~DayThemeServiceTests" -v normal
```
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Services/Theme/DayThemeService.cs tests/FamilyHQ.Services.Tests/Theme/DayThemeServiceTests.cs
git commit -m "feat(theme): add DayThemeService with EnsureToday, RecalculateForToday, and GetToday"
```

---

## Task 6: DayThemeSchedulerService and Service Registration

**Files:**
- Create: `src/FamilyHQ.Services/Theme/DayThemeSchedulerService.cs`
- Modify: `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Implement DayThemeSchedulerService**

```csharp
// src/FamilyHQ.Services/Theme/DayThemeSchedulerService.cs
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Theme;

public class DayThemeSchedulerService : BackgroundService, IDayThemeScheduler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub> _hubContext;
    private readonly ILogger<DayThemeSchedulerService> _logger;
    private CancellationTokenSource _delayCts = new();

    public DayThemeSchedulerService(
        IServiceProvider serviceProvider,
        IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub> hubContext,
        ILogger<DayThemeSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task TriggerRecalculationAsync()
    {
        var old = Interlocked.Exchange(ref _delayCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();
        await dayThemeService.EnsureTodayAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLoopIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Recalculation was triggered — loop restarts to re-read boundaries
            }
        }
    }

    private async Task RunLoopIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();

        var dto = await dayThemeService.GetTodayAsync(stoppingToken);
        var currentPeriod = dto.CurrentPeriod;

        await _hubContext.Clients.All.SendAsync("ThemeChanged", currentPeriod, stoppingToken);
        _logger.LogInformation("Theme broadcast: {Period}", currentPeriod);

        var nextBoundary = GetNextBoundaryDelay(dto);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _delayCts.Token);
        await Task.Delay(nextBoundary, linkedCts.Token);

        // After delay: re-check if it's a new day
        var todayRecord = await dayThemeService.GetTodayAsync(stoppingToken);
        if (todayRecord.Date != DateOnly.FromDateTime(DateTime.Today))
        {
            await dayThemeService.EnsureTodayAsync(stoppingToken);
        }
    }

    private static TimeSpan GetNextBoundaryDelay(Core.DTOs.DayThemeDto dto)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var boundaries = new[] { dto.MorningStart, dto.DaytimeStart, dto.EveningStart, dto.NightStart };
        var next = boundaries.Where(b => b > now).OrderBy(b => b).FirstOrDefault();

        if (next == default)
        {
            // All boundaries passed — wait until midnight
            var midnight = DateTime.Today.AddDays(1);
            return midnight - DateTime.Now;
        }

        var nextDateTime = DateTime.Today.Add(next.ToTimeSpan());
        var delay = nextDateTime - DateTime.Now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
```

- [ ] **Step 2: Register services**

In `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`, add inside `AddFamilyHqServices()`:

```csharp
services.AddSingleton<DayThemeSchedulerService>();
services.AddHostedService(sp => sp.GetRequiredService<DayThemeSchedulerService>());
services.AddSingleton<IDayThemeScheduler>(sp => sp.GetRequiredService<DayThemeSchedulerService>());
services.AddScoped<ISunCalculatorService, SunCalculatorService>();
services.AddScoped<IDayThemeService, DayThemeService>();
```

Add required usings:
```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Theme;
```

Note: `ILocationService` and `IGeocodingService` are registered as typed HttpClients in `Program.cs` (Task 4, Step 5) — do not double-register them here as `AddScoped`.

- [ ] **Step 3: Build Services and WebApi to verify no errors**

```bash
dotnet build src/FamilyHQ.Services/FamilyHQ.Services.csproj && dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Services/
git commit -m "feat(theme): add DayThemeSchedulerService (BackgroundService) with IDayThemeScheduler trigger interface"
```

---

## Task 7: API Controllers

**Files:**
- Create: `src/FamilyHQ.WebApi/Controllers/DayThemeController.cs`
- Create: `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`
- Create: `tests/FamilyHQ.WebApi.Tests/Controllers/DayThemeControllerTests.cs`
- Create: `tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs`

- [ ] **Step 1: Write failing tests for DayThemeController**

```csharp
// tests/FamilyHQ.WebApi.Tests/Controllers/DayThemeControllerTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class DayThemeControllerTests
{
    private readonly Mock<IDayThemeService> _serviceMock = new();

    [Fact]
    public async Task GetToday_ReturnsOk_WithDayThemeDto()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dto = new DayThemeDto(today,
            new TimeOnly(5, 30), new TimeOnly(6, 45), new TimeOnly(20, 15), new TimeOnly(21, 30),
            "Daytime");
        _serviceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var controller = new DayThemeController(_serviceMock.Object);
        var result = await controller.GetToday(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }
}
```

- [ ] **Step 2: Write failing tests for SettingsController**

```csharp
// tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class SettingsControllerTests
{
    private readonly Mock<ILocationSettingRepository> _locationRepoMock = new();
    private readonly Mock<IGeocodingService> _geocodingMock = new();
    private readonly Mock<IDayThemeService> _dayThemeServiceMock = new();
    private readonly Mock<IDayThemeScheduler> _schedulerMock = new();
    private readonly Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> _hubMock = new();

    private SettingsController CreateSut() =>
        new(_locationRepoMock.Object, _geocodingMock.Object, _dayThemeServiceMock.Object,
            _schedulerMock.Object, _hubMock.Object);

    [Fact]
    public async Task GetLocation_Returns404_WhenNotSet()
    {
        _locationRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var result = await CreateSut().GetLocation(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenSet()
    {
        _locationRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { PlaceName = "Edinburgh, Scotland", Latitude = 55.9, Longitude = -3.2 });

        var result = await CreateSut().GetLocation(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LocationSettingDto>()
            .Which.PlaceName.Should().Be("Edinburgh, Scotland");
    }

    [Fact]
    public async Task SaveLocation_Geocodes_SavesPersists_AndTriggers()
    {
        _geocodingMock.Setup(x => x.GeocodeAsync("Edinburgh, Scotland", It.IsAny<CancellationToken>()))
            .ReturnsAsync((55.9533, -3.1883));
        _locationRepoMock.Setup(x => x.UpsertAsync(It.IsAny<LocationSetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting ls, CancellationToken _) => ls);
        _dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(DateOnly.FromDateTime(DateTime.Today),
                new TimeOnly(5,0), new TimeOnly(6,30), new TimeOnly(20,0), new TimeOnly(21,30), "Daytime"));
        _schedulerMock.Setup(x => x.TriggerRecalculationAsync()).Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        var clientMock = new Mock<IClientProxy>();
        clientsMock.Setup(x => x.All).Returns(clientMock.Object);
        _hubMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        var result = await CreateSut().SaveLocation(new SaveLocationRequest("Edinburgh, Scotland"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _schedulerMock.Verify(x => x.TriggerRecalculationAsync(), Times.Once);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/FamilyHQ.WebApi.Tests/ --filter "FullyQualifiedName~DayThemeControllerTests|FullyQualifiedName~SettingsControllerTests" --no-build 2>&1 | tail -5
```
Expected: Build error (controllers not found).

- [ ] **Step 4: Implement DayThemeController**

```csharp
// src/FamilyHQ.WebApi/Controllers/DayThemeController.cs
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DayThemeController : ControllerBase
{
    private readonly IDayThemeService _dayThemeService;

    public DayThemeController(IDayThemeService dayThemeService)
    {
        _dayThemeService = dayThemeService;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var dto = await _dayThemeService.GetTodayAsync(ct);
        return Ok(dto);
    }
}
```

- [ ] **Step 5: Implement SettingsController**

```csharp
// src/FamilyHQ.WebApi/Controllers/SettingsController.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ILocationSettingRepository _locationRepo;
    private readonly IGeocodingService _geocodingService;
    private readonly IDayThemeService _dayThemeService;
    private readonly IDayThemeScheduler _scheduler;
    private readonly IHubContext<CalendarHub> _hubContext;

    public SettingsController(
        ILocationSettingRepository locationRepo,
        IGeocodingService geocodingService,
        IDayThemeService dayThemeService,
        IDayThemeScheduler scheduler,
        IHubContext<CalendarHub> hubContext)
    {
        _locationRepo = locationRepo;
        _geocodingService = geocodingService;
        _dayThemeService = dayThemeService;
        _scheduler = scheduler;
        _hubContext = hubContext;
    }

    [HttpGet("location")]
    public async Task<IActionResult> GetLocation(CancellationToken ct)
    {
        var setting = await _locationRepo.GetAsync(ct);
        if (setting is null) return NotFound();
        return Ok(new LocationSettingDto(setting.PlaceName, IsAutoDetected: false));
    }

    [HttpPost("location")]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationRequest request, CancellationToken ct)
    {
        var (lat, lon) = await _geocodingService.GeocodeAsync(request.PlaceName, ct);

        await _locationRepo.UpsertAsync(new LocationSetting
        {
            PlaceName = request.PlaceName,
            Latitude = lat,
            Longitude = lon,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        await _dayThemeService.RecalculateForTodayAsync(ct);

        var dto = await _dayThemeService.GetTodayAsync(ct);
        await _hubContext.Clients.All.SendAsync("ThemeChanged", dto.CurrentPeriod, ct);

        await _scheduler.TriggerRecalculationAsync();

        return Ok(new LocationSettingDto(request.PlaceName, IsAutoDetected: false));
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/FamilyHQ.WebApi.Tests/ --filter "FullyQualifiedName~DayThemeControllerTests|FullyQualifiedName~SettingsControllerTests" -v normal
```
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/ tests/FamilyHQ.WebApi.Tests/Controllers/
git commit -m "feat(theme): add DayThemeController and SettingsController with location geocoding and theme recalculation"
```

---

## Task 8: CSS Theme System

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

This task **replaces** all colour variables and adds the four theme blocks, layer styles, and component transitions. All existing layout rules (grid layouts, event pills, modal sizing, etc.) are preserved — only colour references change.

- [ ] **Step 1: Replace the CSS custom property declarations and add @property registrations**

At the top of `app.css`, replace any existing `--primary`, `--background`, `--surface`, `--border`, `--text-main`, `--text-muted`, `--weekend-bg`, `--today-bg` variable declarations with the following block (keep all other rules intact):

```css
/* =============================================
   @property registrations — typed <color> values
   allow browser to interpolate during transitions
   ============================================= */
@property --theme-bg-start    { syntax: '<color>'; inherits: true; initial-value: #FFF8F0; }
@property --theme-bg-end      { syntax: '<color>'; inherits: true; initial-value: #FFD8A8; }
@property --theme-surface     { syntax: '<color>'; inherits: true; initial-value: rgba(255,255,255,0.75); }
@property --theme-border      { syntax: '<color>'; inherits: true; initial-value: rgba(124,58,0,0.15); }
@property --theme-text        { syntax: '<color>'; inherits: true; initial-value: #3B1800; }
@property --theme-text-muted  { syntax: '<color>'; inherits: true; initial-value: #7C3A00; }
@property --theme-accent      { syntax: '<color>'; inherits: true; initial-value: #F97316; }
@property --theme-accent-hover{ syntax: '<color>'; inherits: true; initial-value: #EA580C; }
@property --theme-today       { syntax: '<color>'; inherits: true; initial-value: rgba(249,115,22,0.15); }
@property --theme-weekend     { syntax: '<color>'; inherits: true; initial-value: rgba(249,115,22,0.05); }

/* =============================================
   Theme blocks — driven by data-theme on <body>
   ============================================= */
[data-theme="morning"] {
    --theme-bg-start:     #FFF8F0;
    --theme-bg-end:       #FFD8A8;
    --theme-surface:      rgba(255,255,255,0.75);
    --theme-border:       rgba(124,58,0,0.15);
    --theme-text:         #3B1800;
    --theme-text-muted:   #7C3A00;
    --theme-accent:       #F97316;
    --theme-accent-hover: #EA580C;
    --theme-today:        rgba(249,115,22,0.15);
    --theme-weekend:      rgba(249,115,22,0.05);
}

[data-theme="daytime"] {
    --theme-bg-start:     #E8F4FD;
    --theme-bg-end:       #C8E6FA;
    --theme-surface:      rgba(255,255,255,0.80);
    --theme-border:       rgba(26,74,110,0.15);
    --theme-text:         #0F2A40;
    --theme-text-muted:   #1A4A6E;
    --theme-accent:       #3B82F6;
    --theme-accent-hover: #2563EB;
    --theme-today:        rgba(59,130,246,0.15);
    --theme-weekend:      rgba(59,130,246,0.05);
}

[data-theme="evening"] {
    --theme-bg-start:     #2D1B4E;
    --theme-bg-end:       #4A2060;
    --theme-surface:      rgba(255,255,255,0.10);
    --theme-border:       rgba(244,200,122,0.2);
    --theme-text:         #F4E8D0;
    --theme-text-muted:   #F4C87A;
    --theme-accent:       #C084FC;
    --theme-accent-hover: #A855F7;
    --theme-today:        rgba(192,132,252,0.2);
    --theme-weekend:      rgba(192,132,252,0.08);
}

[data-theme="night"] {
    --theme-bg-start:     #0A0E1A;
    --theme-bg-end:       #0F1628;
    --theme-surface:      rgba(255,255,255,0.06);
    --theme-border:       rgba(139,179,232,0.15);
    --theme-text:         #E2EAF4;
    --theme-text-muted:   #8BB3E8;
    --theme-accent:       #60A5FA;
    --theme-accent-hover: #3B82F6;
    --theme-today:        rgba(96,165,250,0.15);
    --theme-weekend:      rgba(96,165,250,0.05);
}

/* =============================================
   DOM layer model
   ============================================= */
html, body {
    margin: 0;
    padding: 0;
    height: 100%;
    /* Background is handled by #theme-bg — do NOT set background here */
    color: var(--theme-text);
    transition: color 45s ease-in-out;
    font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

#theme-bg {
    position: fixed;
    inset: 0;
    z-index: 0;
    background: linear-gradient(160deg, var(--theme-bg-start), var(--theme-bg-end));
    transition:
        --theme-bg-start 45s ease-in-out,
        --theme-bg-end   45s ease-in-out;
    pointer-events: none;
}

#weather-overlay {
    position: fixed;
    inset: 0;
    z-index: 1;
    pointer-events: none;
}

#app {
    position: relative;
    z-index: 2;
    min-height: 100vh;
}
```

- [ ] **Step 2: Replace all hardcoded colours in component rules**

In the same `app.css` file, find and replace all occurrences of the old variables with the new theme variables:

- `var(--background)` → `var(--theme-surface)`
- `var(--surface)` → `var(--theme-surface)`
- `var(--border)` → `var(--theme-border)`
- `var(--text-main)` → `var(--theme-text)`
- `var(--text-muted)` → `var(--theme-text-muted)`
- `var(--primary)` → `var(--theme-accent)`
- `var(--today-bg)` → `var(--theme-today)`
- `var(--weekend-bg)` → `var(--theme-weekend)`

Also replace any remaining hardcoded hex colours (e.g. `#fff`, `#333`, Bootstrap colour classes converted to inline styles) with the appropriate theme variable.

Add `transition: background-color 45s ease-in-out, color 45s ease-in-out, border-color 45s ease-in-out` to any rule that was previously using the old static variables for always-visible components (calendar cells, header, panels).

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(theme): replace CSS colour system with time-of-day theme variables and @property transitions"
```

---

## Task 9: Frontend Theme Infrastructure (theme.js, index.html, ThemeService, SignalRService)

**Files:**
- Create: `src/FamilyHQ.WebUi/wwwroot/js/theme.js`
- Modify: `src/FamilyHQ.WebUi/wwwroot/index.html`
- Create: `src/FamilyHQ.WebUi/Services/IThemeService.cs`
- Create: `src/FamilyHQ.WebUi/Services/ThemeService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/SignalRService.cs`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

- [ ] **Step 1: Create theme.js**

```js
// src/FamilyHQ.WebUi/wwwroot/js/theme.js
export function setTheme(period) {
    document.body.setAttribute('data-theme', period.toLowerCase());
}
```

- [ ] **Step 2: Update index.html**

In `src/FamilyHQ.WebUi/wwwroot/index.html`:

1. Add the two layer divs immediately after the opening `<body>` tag:
```html
<body>
    <div id="theme-bg" aria-hidden="true"></div>
    <div id="weather-overlay" aria-hidden="true"></div>
    <div id="app">Loading...</div>
```

2. Add the theme.js module import before the closing `</body>` tag (after the existing `<script>` tags):
```html
    <script type="module" src="js/theme.js"></script>
```

- [ ] **Step 3: Add OnThemeChanged event to SignalRService**

In `src/FamilyHQ.WebUi/Services/SignalRService.cs`:

1. Add the event field after `OnEventsUpdated`:
```csharp
public event Action<string>? OnThemeChanged;
```

2. In the constructor (inside the hub connection setup), add the handler after the existing `On` registration:
```csharp
_hubConnection.On<string>("ThemeChanged", period => OnThemeChanged?.Invoke(period));
```

- [ ] **Step 4: Create IThemeService and ThemeService**

```csharp
// src/FamilyHQ.WebUi/Services/IThemeService.cs
namespace FamilyHQ.WebUi.Services;

public interface IThemeService
{
    Task InitialiseAsync();
}
```

```csharp
// src/FamilyHQ.WebUi/Services/ThemeService.cs
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class ThemeService : IThemeService
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly SignalRService _signalRService;

    public ThemeService(HttpClient httpClient, IJSRuntime jsRuntime, SignalRService signalRService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;

        _signalRService.OnThemeChanged += async period => await SetThemeAsync(period);
    }

    public async Task InitialiseAsync()
    {
        var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
        if (dto is not null)
            await SetThemeAsync(dto.CurrentPeriod);
    }

    private async Task SetThemeAsync(string period)
    {
        var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await module.InvokeVoidAsync("setTheme", period);
    }
}
```

- [ ] **Step 5: Register ThemeService in WebUi Program.cs**

In `src/FamilyHQ.WebUi/Program.cs`, add after the existing service registrations:

```csharp
builder.Services.AddScoped<IThemeService, ThemeService>();
```

Add the required using:
```csharp
using FamilyHQ.WebUi.Services;
```

- [ ] **Step 6: Build WebUi to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/js/ src/FamilyHQ.WebUi/wwwroot/index.html src/FamilyHQ.WebUi/Services/ src/FamilyHQ.WebUi/Program.cs
git commit -m "feat(theme): add ThemeService, theme.js, SignalR ThemeChanged handler, and DOM layer divs"
```

---

## Task 10: MainLayout and DashboardHeader Updates

**Files:**
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor`

- [ ] **Step 1: Update MainLayout.razor**

In `src/FamilyHQ.WebUi/Layout/MainLayout.razor`, inject and call ThemeService:

1. Add inject directive at the top:
```razor
@inject IThemeService ThemeService
```

2. Add `OnInitializedAsync` override (or add to existing one if present):
```razor
@code {
    protected override async Task OnInitializedAsync()
    {
        await ThemeService.InitialiseAsync();
    }
}
```

Ensure the using is present:
```razor
@using FamilyHQ.WebUi.Services
```

- [ ] **Step 2: Update DashboardHeader.razor**

Replace the content of `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor` with a simplified version:

The header should contain:
- Brand name on the left
- Settings gear icon (linking to `/settings`) on the right
- No username display
- No sign-out button

```razor
@using Microsoft.AspNetCore.Components.Routing

<header class="dashboard-header">
    <span class="dashboard-header__brand">FamilyHQ</span>
    <a href="/settings" class="dashboard-header__settings" aria-label="Settings">
        <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24"
             fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="3"/>
            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06
                     a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09
                     A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83
                     l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09
                     A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83
                     l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09
                     a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83
                     l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09
                     a1.65 1.65 0 0 0-1.51 1z"/>
        </svg>
    </a>
</header>
```

Add CSS for the header to `app.css`:

```css
.dashboard-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px 20px;
    background: var(--theme-surface);
    border-bottom: 1px solid var(--theme-border);
    color: var(--theme-text);
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out, color 45s ease-in-out;
}

.dashboard-header__brand {
    font-size: 1.4rem;
    font-weight: 600;
    letter-spacing: 0.02em;
}

.dashboard-header__settings {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 48px;
    height: 48px;
    color: var(--theme-text);
    text-decoration: none;
    border-radius: 50%;
    transition: background-color 0.2s ease;
}

.dashboard-header__settings:hover {
    background: var(--theme-accent);
    color: #fff;
}
```

- [ ] **Step 3: Build WebUi to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Layout/ src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(theme): update MainLayout to init ThemeService; simplify DashboardHeader to brand + settings gear"
```

---

## Task 11: Settings Page

**Files:**
- Create: `src/FamilyHQ.WebUi/Services/ISettingsApiService.cs`
- Create: `src/FamilyHQ.WebUi/Services/SettingsApiService.cs`
- Create: `src/FamilyHQ.WebUi/Pages/Settings.razor`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

- [ ] **Step 1: Create ISettingsApiService and SettingsApiService**

```csharp
// src/FamilyHQ.WebUi/Services/ISettingsApiService.cs
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface ISettingsApiService
{
    Task<LocationSettingDto?> GetLocationAsync();
    Task<LocationSettingDto> SaveLocationAsync(string placeName);
    Task<DayThemeDto> GetTodayThemeAsync();
}
```

```csharp
// src/FamilyHQ.WebUi/Services/SettingsApiService.cs
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public class SettingsApiService : ISettingsApiService
{
    private readonly HttpClient _httpClient;

    public SettingsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LocationSettingDto?> GetLocationAsync()
    {
        var response = await _httpClient.GetAsync("api/settings/location");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationSettingDto>();
    }

    public async Task<LocationSettingDto> SaveLocationAsync(string placeName)
    {
        var response = await _httpClient.PostAsJsonAsync("api/settings/location", new SaveLocationRequest(placeName));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationSettingDto>())!;
    }

    public async Task<DayThemeDto> GetTodayThemeAsync()
    {
        return (await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today"))!;
    }
}
```

- [ ] **Step 2: Register SettingsApiService in Program.cs**

In `src/FamilyHQ.WebUi/Program.cs`, add:
```csharp
builder.Services.AddScoped<ISettingsApiService, SettingsApiService>();
```

- [ ] **Step 3: Create Settings.razor page**

```razor
@* src/FamilyHQ.WebUi/Pages/Settings.razor *@
@page "/settings"
@using FamilyHQ.Core.DTOs
@using FamilyHQ.WebUi.Services
@using Microsoft.AspNetCore.Components.Authorization
@inject ISettingsApiService SettingsApiService
@inject IJSRuntime JS
@inject AuthenticationStateProvider AuthStateProvider
@inject NavigationManager Navigation

<div class="settings-page">

    <h2 class="settings-section__title">Location</h2>
    <div class="settings-section">
        @if (_locationSetting is not null)
        {
            <div class="settings-location-pill">
                <span>@_locationSetting.PlaceName</span>
                <span class="settings-location-pill__badge @(_locationSetting.IsAutoDetected ? "badge--auto" : "badge--saved")">
                    @(_locationSetting.IsAutoDetected ? "Auto" : "Saved")
                </span>
            </div>
        }
        else
        {
            <p class="settings-hint">No location saved — using automatic detection.</p>
        }

        <div class="settings-input-container" @ref="_scrollContainer">
            <label for="place-input" class="settings-label">Place name</label>
            <input id="place-input"
                   type="text"
                   class="settings-input"
                   @bind="_placeNameInput"
                   @bind:event="oninput"
                   @onfocus="OnInputFocus"
                   placeholder="e.g. Edinburgh, Scotland" />
        </div>

        @if (!string.IsNullOrWhiteSpace(_saveError))
        {
            <p class="settings-error">@_saveError</p>
        }

        <button class="settings-btn" @onclick="SaveLocationAsync" disabled="@_isSaving">
            @(_isSaving ? "Saving…" : (_locationSetting is { IsAutoDetected: false } ? "Update Location" : "Save Location"))
        </button>
        <p class="settings-hint">Takes effect immediately.</p>
    </div>

    <h2 class="settings-section__title">Today's Theme Schedule</h2>
    <div class="settings-section settings-theme-schedule">
        @if (_todayTheme is not null)
        {
            <div class="theme-tile theme-tile--morning">
                <span class="theme-tile__name">Morning</span>
                <span class="theme-tile__time">@_todayTheme.MorningStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--daytime">
                <span class="theme-tile__name">Daytime</span>
                <span class="theme-tile__time">@_todayTheme.DaytimeStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--evening">
                <span class="theme-tile__name">Evening</span>
                <span class="theme-tile__time">@_todayTheme.EveningStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--night">
                <span class="theme-tile__name">Night</span>
                <span class="theme-tile__time">@_todayTheme.NightStart.ToString("HH:mm")</span>
            </div>
        }
    </div>

    <h2 class="settings-section__title">Account</h2>
    <div class="settings-section settings-account">
        <AuthorizeView>
            <Authorized>
                <div class="account-avatar">@GetInitials(context.User.Identity?.Name)</div>
                <p class="account-name">@context.User.Identity?.Name</p>
                <p class="account-email">@context.User.FindFirst("email")?.Value</p>
            </Authorized>
        </AuthorizeView>
        <form method="post" action="authentication/logout">
            <button type="submit" class="settings-btn settings-btn--secondary">Sign Out</button>
        </form>
    </div>

</div>

@code {
    private LocationSettingDto? _locationSetting;
    private DayThemeDto? _todayTheme;
    private string _placeNameInput = string.Empty;
    private bool _isSaving;
    private string? _saveError;
    private ElementReference _scrollContainer;

    protected override async Task OnInitializedAsync()
    {
        _locationSetting = await SettingsApiService.GetLocationAsync();
        _todayTheme = await SettingsApiService.GetTodayThemeAsync();
    }

    private async Task OnInputFocus()
    {
        await JS.InvokeVoidAsync("eval",
            "document.getElementById('place-input')?.scrollIntoView({ behavior: 'smooth', block: 'center' })");
    }

    private async Task SaveLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(_placeNameInput)) return;

        _isSaving = true;
        _saveError = null;
        try
        {
            _locationSetting = await SettingsApiService.SaveLocationAsync(_placeNameInput);
            _placeNameInput = string.Empty;
            _todayTheme = await SettingsApiService.GetTodayThemeAsync();
        }
        catch (Exception ex)
        {
            _saveError = $"Could not save location: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }
}
```

- [ ] **Step 4: Add Settings page CSS to app.css**

Append to `app.css`:

```css
/* =============================================
   Settings page
   ============================================= */
.settings-page {
    max-width: 700px;
    margin: 0 auto;
    padding: 24px 20px 80px;
    overflow-y: auto;
    /* Virtual keyboard inset */
    padding-bottom: max(80px, env(keyboard-inset-height, 0px));
}

.settings-section__title {
    font-size: 1.1rem;
    font-weight: 600;
    color: var(--theme-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.08em;
    margin: 32px 0 12px;
}

.settings-section {
    background: var(--theme-surface);
    border: 1px solid var(--theme-border);
    border-radius: 12px;
    padding: 20px;
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out;
}

.settings-location-pill {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    background: var(--theme-accent);
    color: #fff;
    border-radius: 999px;
    padding: 6px 14px;
    font-size: 0.95rem;
    margin-bottom: 16px;
}

.settings-location-pill__badge {
    font-size: 0.75rem;
    padding: 2px 8px;
    border-radius: 999px;
    background: rgba(0,0,0,0.25);
}

.settings-input-container {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-bottom: 12px;
}

.settings-label {
    font-size: 0.9rem;
    color: var(--theme-text-muted);
}

.settings-input {
    background: transparent;
    border: 1px solid var(--theme-border);
    border-radius: 8px;
    padding: 12px 14px;
    font-size: 1rem;
    color: var(--theme-text);
    outline: none;
    min-height: 48px;
    transition: border-color 0.2s ease;
}

.settings-input:focus {
    border-color: var(--theme-accent);
}

.settings-btn {
    display: block;
    width: 100%;
    min-height: 48px;
    background: var(--theme-accent);
    color: #fff;
    border: none;
    border-radius: 10px;
    font-size: 1rem;
    font-weight: 600;
    cursor: pointer;
    margin-top: 8px;
    transition: background-color 0.2s ease;
}

.settings-btn:hover:not(:disabled) {
    background: var(--theme-accent-hover);
}

.settings-btn:disabled {
    opacity: 0.6;
    cursor: not-allowed;
}

.settings-btn--secondary {
    background: transparent;
    border: 1px solid var(--theme-border);
    color: var(--theme-text);
    margin-top: 16px;
}

.settings-hint {
    font-size: 0.85rem;
    color: var(--theme-text-muted);
    margin: 6px 0 0;
}

.settings-error {
    color: #ef4444;
    font-size: 0.9rem;
    margin: 4px 0;
}

/* Theme schedule tiles */
.settings-theme-schedule {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
}

.theme-tile {
    border-radius: 10px;
    padding: 14px 16px;
    display: flex;
    flex-direction: column;
    gap: 4px;
}

.theme-tile--morning  { background: rgba(249,115,22,0.15);  }
.theme-tile--daytime  { background: rgba(59,130,246,0.15);  }
.theme-tile--evening  { background: rgba(192,132,252,0.15); }
.theme-tile--night    { background: rgba(96,165,250,0.10);  }

.theme-tile__name {
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--theme-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.06em;
}

.theme-tile__time {
    font-size: 1.4rem;
    font-weight: 700;
    color: var(--theme-text);
}

/* Account section */
.settings-account {
    text-align: center;
}

.account-avatar {
    width: 64px;
    height: 64px;
    border-radius: 50%;
    background: var(--theme-accent);
    color: #fff;
    font-size: 1.4rem;
    font-weight: 700;
    display: flex;
    align-items: center;
    justify-content: center;
    margin: 0 auto 12px;
}

.account-name {
    font-size: 1.1rem;
    font-weight: 600;
    color: var(--theme-text);
    margin: 0 0 4px;
}

.account-email {
    font-size: 0.9rem;
    color: var(--theme-text-muted);
    margin: 0 0 16px;
}
```

- [ ] **Step 5: Build WebUi to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Full project build**

> **REQUIRES APPROVAL** — runs full project build.

```bash
dotnet build FamilyHQ.sln
```
Expected: Build succeeded, 0 errors, 0 warnings (or only pre-existing warnings).

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Settings.razor src/FamilyHQ.WebUi/Services/ISettingsApiService.cs src/FamilyHQ.WebUi/Services/SettingsApiService.cs src/FamilyHQ.WebUi/wwwroot/css/app.css src/FamilyHQ.WebUi/Program.cs
git commit -m "feat(theme): add Settings page with location, theme schedule, and account sections"
```

---

## Final Integration Commit

After all tasks pass and the full build succeeds:

```bash
git log --oneline -12
```

Verify all 11 feature commits are present. Then open a PR from the feature branch to `master`.
