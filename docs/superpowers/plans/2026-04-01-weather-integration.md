# Weather Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate real-time weather data from Open-Meteo into FamilyHQ, displaying current conditions, forecasts, and animated weather overlays across all dashboard views.

**Architecture:** A `WeatherPollerService` (BackgroundService) polls a weather provider at configurable intervals, stores data in PostgreSQL, and broadcasts `WeatherUpdated` via SignalR. The Blazor WASM frontend subscribes, fetches weather data via HTTP, and renders weather widgets + CSS-only overlay animations. The provider is abstracted behind a URL — same `OpenMeteoWeatherProvider` code hits the real API in production and the simulator in dev/staging.

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core, PostgreSQL, SignalR, CSS animations, Open-Meteo API, Reqnroll BDD

**Spec:** `docs/superpowers/specs/2026-04-01-weather-integration-design.md`

---

## Phase 1: Core Domain Models

### Task 1: WeatherCondition Enum and TemperatureUnit Enum

**Files:**
- Create: `src/FamilyHQ.Core/Enums/WeatherCondition.cs`
- Create: `src/FamilyHQ.Core/Enums/TemperatureUnit.cs`
- Create: `src/FamilyHQ.Core/Enums/WeatherDataType.cs`

- [ ] **Step 1: Create WeatherCondition enum**

```csharp
namespace FamilyHQ.Core.Enums;

public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    LightRain,
    HeavyRain,
    Thunder,
    Snow,
    Sleet
}
```

- [ ] **Step 2: Create TemperatureUnit enum**

```csharp
namespace FamilyHQ.Core.Enums;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}
```

- [ ] **Step 3: Create WeatherDataType enum**

```csharp
namespace FamilyHQ.Core.Enums;

public enum WeatherDataType
{
    Current,
    Hourly,
    Daily
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/FamilyHQ.Core/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Core/Enums/WeatherCondition.cs src/FamilyHQ.Core/Enums/TemperatureUnit.cs src/FamilyHQ.Core/Enums/WeatherDataType.cs
git commit -m "feat(weather): add WeatherCondition, TemperatureUnit, and WeatherDataType enums"
```

---

### Task 2: WeatherDataPoint and WeatherSetting Entities

**Files:**
- Create: `src/FamilyHQ.Core/Models/WeatherDataPoint.cs`
- Create: `src/FamilyHQ.Core/Models/WeatherSetting.cs`

- [ ] **Step 1: Create WeatherDataPoint entity**

```csharp
namespace FamilyHQ.Core.Models;

using FamilyHQ.Core.Enums;

public class WeatherDataPoint
{
    public int Id { get; set; }
    public int LocationSettingId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public WeatherCondition Condition { get; set; }
    public double TemperatureCelsius { get; set; }
    public double? HighCelsius { get; set; }
    public double? LowCelsius { get; set; }
    public double WindSpeedKmh { get; set; }
    public bool IsWindy { get; set; }
    public WeatherDataType DataType { get; set; }
    public DateTimeOffset RetrievedAt { get; set; }
}
```

- [ ] **Step 2: Create WeatherSetting entity**

```csharp
namespace FamilyHQ.Core.Models;

using FamilyHQ.Core.Enums;

public class WeatherSetting
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 30;
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public double WindThresholdKmh { get; set; } = 30;
    public string? ApiKey { get; set; }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.Core/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Core/Models/WeatherDataPoint.cs src/FamilyHQ.Core/Models/WeatherSetting.cs
git commit -m "feat(weather): add WeatherDataPoint and WeatherSetting entities"
```

---

### Task 3: Weather DTOs

**Files:**
- Create: `src/FamilyHQ.Core/DTOs/CurrentWeatherDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/HourlyForecastItemDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/DailyForecastItemDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/WeatherSettingDto.cs`
- Create: `src/FamilyHQ.Core/DTOs/WeatherResponse.cs`

- [ ] **Step 1: Create CurrentWeatherDto**

```csharp
namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record CurrentWeatherDto(
    WeatherCondition Condition,
    double Temperature,
    bool IsWindy,
    double WindSpeedKmh,
    string IconName);
```

- [ ] **Step 2: Create HourlyForecastItemDto**

```csharp
namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record HourlyForecastItemDto(
    DateTimeOffset Hour,
    WeatherCondition Condition,
    double Temperature,
    bool IsWindy,
    string IconName);
```

- [ ] **Step 3: Create DailyForecastItemDto**

```csharp
namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record DailyForecastItemDto(
    DateOnly Date,
    WeatherCondition Condition,
    double High,
    double Low,
    bool IsWindy,
    string IconName);
```

- [ ] **Step 4: Create WeatherSettingDto**

```csharp
namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherSettingDto(
    bool Enabled,
    int PollIntervalMinutes,
    TemperatureUnit TemperatureUnit,
    double WindThresholdKmh,
    string? ApiKey);
```

- [ ] **Step 5: Create WeatherResponse (provider response model)**

```csharp
namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherResponse(
    WeatherCondition CurrentCondition,
    double CurrentTemperatureCelsius,
    double CurrentWindSpeedKmh,
    List<WeatherHourlyItem> HourlyForecasts,
    List<WeatherDailyItem> DailyForecasts);

public record WeatherHourlyItem(
    DateTimeOffset Time,
    WeatherCondition Condition,
    double TemperatureCelsius,
    double WindSpeedKmh);

public record WeatherDailyItem(
    DateOnly Date,
    WeatherCondition Condition,
    double HighCelsius,
    double LowCelsius,
    double WindSpeedMaxKmh);
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/FamilyHQ.Core/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.Core/DTOs/CurrentWeatherDto.cs src/FamilyHQ.Core/DTOs/HourlyForecastItemDto.cs src/FamilyHQ.Core/DTOs/DailyForecastItemDto.cs src/FamilyHQ.Core/DTOs/WeatherSettingDto.cs src/FamilyHQ.Core/DTOs/WeatherResponse.cs
git commit -m "feat(weather): add weather DTOs and WeatherResponse provider model"
```

---

### Task 4: IWeatherProvider Interface

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs`

- [ ] **Step 1: Create IWeatherProvider interface**

```csharp
namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.DTOs;

public interface IWeatherProvider
{
    Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.Core/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs
git commit -m "feat(weather): add IWeatherProvider interface"
```

---

### Task 5: WeatherSetting Validator

**Files:**
- Create: `src/FamilyHQ.Core/Validators/WeatherSettingDtoValidator.cs`
- Test: `tests/FamilyHQ.Core.Tests/Validators/WeatherSettingDtoValidatorTests.cs`

- [ ] **Step 1: Check if test project exists and find its structure**

Run: `ls tests/` and check for existing test projects. If `FamilyHQ.Core.Tests` doesn't exist, check where validators are tested.

- [ ] **Step 2: Write the failing test**

Create the test file in the appropriate test project (create if needed):

```csharp
namespace FamilyHQ.Core.Tests.Validators;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Validators;
using FluentAssertions;

public class WeatherSettingDtoValidatorTests
{
    private readonly WeatherSettingDtoValidator _sut = new();

    [Fact]
    public void Valid_settings_pass_validation()
    {
        var dto = new WeatherSettingDto(true, 30, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Poll_interval_below_minimum_fails(int interval)
    {
        var dto = new WeatherSettingDto(true, interval, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PollIntervalMinutes");
    }

    [Fact]
    public void Poll_interval_of_one_passes()
    {
        var dto = new WeatherSettingDto(true, 1, TemperatureUnit.Celsius, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Wind_threshold_zero_or_negative_fails(double threshold)
    {
        var dto = new WeatherSettingDto(true, 30, TemperatureUnit.Celsius, threshold, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "WindThresholdKmh");
    }

    [Fact]
    public void Invalid_temperature_unit_fails()
    {
        var dto = new WeatherSettingDto(true, 30, (TemperatureUnit)99, 30, null);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TemperatureUnit");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Core.Tests/ --filter "WeatherSettingDtoValidatorTests" -v n`
Expected: FAIL — `WeatherSettingDtoValidator` not found

- [ ] **Step 4: Create the validator**

```csharp
namespace FamilyHQ.Core.Validators;

using FluentValidation;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;

public class WeatherSettingDtoValidator : AbstractValidator<WeatherSettingDto>
{
    public WeatherSettingDtoValidator()
    {
        RuleFor(x => x.PollIntervalMinutes)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Poll interval must be at least 1 minute.");

        RuleFor(x => x.WindThresholdKmh)
            .GreaterThan(0)
            .WithMessage("Wind threshold must be greater than 0 km/h.");

        RuleFor(x => x.TemperatureUnit)
            .IsInEnum()
            .WithMessage("Temperature unit must be a valid value.");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Core.Tests/ --filter "WeatherSettingDtoValidatorTests" -v n`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Core/Validators/WeatherSettingDtoValidator.cs tests/FamilyHQ.Core.Tests/
git commit -m "feat(weather): add WeatherSettingDto validator with tests"
```

---

## Phase 2: Data Layer

### Task 6: WeatherDataPoint and WeatherSetting EF Core Configuration

**Files:**
- Create: `src/FamilyHQ.Data/Configurations/WeatherDataPointConfiguration.cs`
- Create: `src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs`
- Modify: `src/FamilyHQ.Data/FamilyHqDbContext.cs`

- [ ] **Step 1: Create WeatherDataPointConfiguration**

```csharp
namespace FamilyHQ.Data.Configurations;

using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class WeatherDataPointConfiguration : IEntityTypeConfiguration<WeatherDataPoint>
{
    public void Configure(EntityTypeBuilder<WeatherDataPoint> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LocationSettingId).IsRequired();
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.Condition).IsRequired();
        builder.Property(x => x.TemperatureCelsius).IsRequired();
        builder.Property(x => x.WindSpeedKmh).IsRequired();
        builder.Property(x => x.IsWindy).IsRequired();
        builder.Property(x => x.DataType).IsRequired();
        builder.Property(x => x.RetrievedAt).IsRequired();

        builder.HasIndex(x => new { x.LocationSettingId, x.DataType, x.Timestamp });

        builder.HasOne<LocationSetting>()
            .WithMany()
            .HasForeignKey(x => x.LocationSettingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Create WeatherSettingConfiguration**

```csharp
namespace FamilyHQ.Data.Configurations;

using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class WeatherSettingConfiguration : IEntityTypeConfiguration<WeatherSetting>
{
    public void Configure(EntityTypeBuilder<WeatherSetting> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.PollIntervalMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(x => x.TemperatureUnit).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.WindThresholdKmh).IsRequired().HasDefaultValue(30.0);
        builder.Property(x => x.ApiKey).HasMaxLength(500);
    }
}
```

- [ ] **Step 3: Add DbSets to FamilyHqDbContext**

Add to `src/FamilyHQ.Data/FamilyHqDbContext.cs` after existing DbSet properties:

```csharp
public DbSet<WeatherDataPoint> WeatherDataPoints => Set<WeatherDataPoint>();
public DbSet<WeatherSetting> WeatherSettings => Set<WeatherSetting>();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/FamilyHQ.Data/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Data/Configurations/WeatherDataPointConfiguration.cs src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs src/FamilyHQ.Data/FamilyHqDbContext.cs
git commit -m "feat(weather): add EF Core configurations and DbSets for weather entities"
```

---

### Task 7: Database Migration

**Files:**
- Create: `src/FamilyHQ.Data.PostgreSQL/Migrations/<timestamp>_AddWeatherTables.cs` (auto-generated)

- [ ] **Step 1: Generate migration**

Run from repo root:
```bash
dotnet ef migrations add AddWeatherTables --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```
Expected: Migration file created in `src/FamilyHQ.Data.PostgreSQL/Migrations/`

- [ ] **Step 2: Review the generated migration**

Open the generated file and verify it creates:
- `WeatherDataPoints` table with all columns and the composite index
- `WeatherSettings` table with default values
- Foreign key from `WeatherDataPoints.LocationSettingId` to `LocationSettings.Id`

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.Data.PostgreSQL/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git commit -m "feat(weather): add database migration for weather tables"
```

---

### Task 8: Weather Repositories

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherDataPointRepository.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs`
- Create: `src/FamilyHQ.Data/Repositories/WeatherDataPointRepository.cs`
- Create: `src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs`
- Modify: `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create IWeatherDataPointRepository**

```csharp
namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;

public interface IWeatherDataPointRepository
{
    Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days, CancellationToken ct = default);
    Task ReplaceAllAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create IWeatherSettingRepository**

```csharp
namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWeatherSettingRepository
{
    Task<WeatherSetting> GetOrCreateAsync(CancellationToken ct = default);
    Task<WeatherSetting> UpsertAsync(WeatherSetting setting, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create WeatherDataPointRepository**

```csharp
namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

public class WeatherDataPointRepository(FamilyHqDbContext context) : IWeatherDataPointRepository
{
    public async Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default)
        => await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId && x.DataType == WeatherDataType.Current)
            .OrderByDescending(x => x.RetrievedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date, CancellationToken ct = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId
                && x.DataType == WeatherDataType.Hourly
                && x.Timestamp >= start
                && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days, CancellationToken ct = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var end = today.AddDays(days);

        return await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId
                && x.DataType == WeatherDataType.Daily
                && x.Timestamp >= today
                && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);
    }

    public async Task ReplaceAllAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default)
    {
        var existing = await context.WeatherDataPoints
            .Where(x => x.LocationSettingId == locationSettingId)
            .ToListAsync(ct);

        context.WeatherDataPoints.RemoveRange(existing);
        context.WeatherDataPoints.AddRange(dataPoints);
        await context.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Create WeatherSettingRepository**

```csharp
namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

public class WeatherSettingRepository(FamilyHqDbContext context) : IWeatherSettingRepository
{
    public async Task<WeatherSetting> GetOrCreateAsync(CancellationToken ct = default)
    {
        var setting = await context.WeatherSettings.FirstOrDefaultAsync(ct);
        if (setting is not null)
            return setting;

        setting = new WeatherSetting();
        context.WeatherSettings.Add(setting);
        await context.SaveChangesAsync(ct);
        return setting;
    }

    public async Task<WeatherSetting> UpsertAsync(WeatherSetting setting, CancellationToken ct = default)
    {
        var existing = await context.WeatherSettings.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            context.WeatherSettings.Add(setting);
            await context.SaveChangesAsync(ct);
            return setting;
        }

        existing.Enabled = setting.Enabled;
        existing.PollIntervalMinutes = setting.PollIntervalMinutes;
        existing.TemperatureUnit = setting.TemperatureUnit;
        existing.WindThresholdKmh = setting.WindThresholdKmh;
        existing.ApiKey = setting.ApiKey;
        await context.SaveChangesAsync(ct);
        return existing;
    }
}
```

- [ ] **Step 5: Register repositories in DI**

Add to `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs` inside `AddPostgreSqlDataAccess`:

```csharp
services.AddScoped<IWeatherDataPointRepository, WeatherDataPointRepository>();
services.AddScoped<IWeatherSettingRepository, WeatherSettingRepository>();
```

Add the using statements:

```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Data.Repositories;
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/FamilyHQ.Data.PostgreSQL/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IWeatherDataPointRepository.cs src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs src/FamilyHQ.Data/Repositories/WeatherDataPointRepository.cs src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs
git commit -m "feat(weather): add weather repositories and DI registration"
```

---

## Phase 3: Services Layer

### Task 9: WeatherOptions Configuration Class

**Files:**
- Create: `src/FamilyHQ.Services/Options/WeatherOptions.cs`

- [ ] **Step 1: Create WeatherOptions**

```csharp
namespace FamilyHQ.Services.Options;

public class WeatherOptions
{
    public const string SectionName = "Weather";

    public string BaseUrl { get; set; } = "https://api.open-meteo.com";
    public int PollIntervalMinutes { get; set; } = 30;
    public int MinPollIntervalMinutes { get; set; } = 1;
    public double WindThresholdKmh { get; set; } = 30;
    public bool Enabled { get; set; } = true;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.Services/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.Services/Options/WeatherOptions.cs
git commit -m "feat(weather): add WeatherOptions configuration class"
```

---

### Task 10: WMO Code Mapper

**Files:**
- Create: `src/FamilyHQ.Services/Weather/WmoCodeMapper.cs`
- Create: `tests/FamilyHQ.Services.Tests/Weather/WmoCodeMapperTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WmoCodeMapperTests
{
    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.PartlyCloudy)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Cloudy)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(48, WeatherCondition.Fog)]
    [InlineData(51, WeatherCondition.Drizzle)]
    [InlineData(53, WeatherCondition.Drizzle)]
    [InlineData(55, WeatherCondition.Drizzle)]
    [InlineData(56, WeatherCondition.Sleet)]
    [InlineData(57, WeatherCondition.Sleet)]
    [InlineData(61, WeatherCondition.LightRain)]
    [InlineData(63, WeatherCondition.HeavyRain)]
    [InlineData(65, WeatherCondition.HeavyRain)]
    [InlineData(66, WeatherCondition.Sleet)]
    [InlineData(67, WeatherCondition.Sleet)]
    [InlineData(71, WeatherCondition.Snow)]
    [InlineData(73, WeatherCondition.Snow)]
    [InlineData(75, WeatherCondition.Snow)]
    [InlineData(77, WeatherCondition.Snow)]
    [InlineData(80, WeatherCondition.LightRain)]
    [InlineData(81, WeatherCondition.HeavyRain)]
    [InlineData(82, WeatherCondition.HeavyRain)]
    [InlineData(85, WeatherCondition.Snow)]
    [InlineData(86, WeatherCondition.Snow)]
    [InlineData(95, WeatherCondition.Thunder)]
    [InlineData(96, WeatherCondition.Thunder)]
    [InlineData(99, WeatherCondition.Thunder)]
    public void Maps_wmo_code_to_correct_condition(int wmoCode, WeatherCondition expected)
    {
        var result = WmoCodeMapper.ToCondition(wmoCode);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(50)]
    public void Unknown_wmo_code_returns_clear(int wmoCode)
    {
        var result = WmoCodeMapper.ToCondition(wmoCode);
        result.Should().Be(WeatherCondition.Clear);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "WmoCodeMapperTests" -v n`
Expected: FAIL — `WmoCodeMapper` not found

- [ ] **Step 3: Create WmoCodeMapper**

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;

public static class WmoCodeMapper
{
    public static WeatherCondition ToCondition(int wmoCode) => wmoCode switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        51 or 53 or 55 => WeatherCondition.Drizzle,
        56 or 57 => WeatherCondition.Sleet,
        61 or 80 => WeatherCondition.LightRain,
        63 or 65 or 81 or 82 => WeatherCondition.HeavyRain,
        66 or 67 => WeatherCondition.Sleet,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherCondition.Snow,
        95 or 96 or 99 => WeatherCondition.Thunder,
        _ => WeatherCondition.Clear
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "WmoCodeMapperTests" -v n`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Services/Weather/WmoCodeMapper.cs tests/FamilyHQ.Services.Tests/
git commit -m "feat(weather): add WMO code to WeatherCondition mapper with tests"
```

---

### Task 11: Weather Icon Mapper

**Files:**
- Create: `src/FamilyHQ.Services/Weather/WeatherIconMapper.cs`
- Create: `tests/FamilyHQ.Services.Tests/Weather/WeatherIconMapperTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class WeatherIconMapperTests
{
    [Theory]
    [InlineData(WeatherCondition.Clear, "clear")]
    [InlineData(WeatherCondition.PartlyCloudy, "partly-cloudy")]
    [InlineData(WeatherCondition.Cloudy, "cloudy")]
    [InlineData(WeatherCondition.Fog, "fog")]
    [InlineData(WeatherCondition.Drizzle, "drizzle")]
    [InlineData(WeatherCondition.LightRain, "light-rain")]
    [InlineData(WeatherCondition.HeavyRain, "heavy-rain")]
    [InlineData(WeatherCondition.Thunder, "thunder")]
    [InlineData(WeatherCondition.Snow, "snow")]
    [InlineData(WeatherCondition.Sleet, "sleet")]
    public void Maps_condition_to_icon_name(WeatherCondition condition, string expectedIcon)
    {
        var result = WeatherIconMapper.ToIconName(condition);
        result.Should().Be(expectedIcon);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "WeatherIconMapperTests" -v n`
Expected: FAIL — `WeatherIconMapper` not found

- [ ] **Step 3: Create WeatherIconMapper**

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;

public static class WeatherIconMapper
{
    public static string ToIconName(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => "clear",
        WeatherCondition.PartlyCloudy => "partly-cloudy",
        WeatherCondition.Cloudy => "cloudy",
        WeatherCondition.Fog => "fog",
        WeatherCondition.Drizzle => "drizzle",
        WeatherCondition.LightRain => "light-rain",
        WeatherCondition.HeavyRain => "heavy-rain",
        WeatherCondition.Thunder => "thunder",
        WeatherCondition.Snow => "snow",
        WeatherCondition.Sleet => "sleet",
        _ => "clear"
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "WeatherIconMapperTests" -v n`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Services/Weather/WeatherIconMapper.cs tests/FamilyHQ.Services.Tests/
git commit -m "feat(weather): add WeatherCondition to icon name mapper with tests"
```

---

### Task 12: Temperature Converter

**Files:**
- Create: `src/FamilyHQ.Services/Weather/TemperatureConverter.cs`
- Create: `tests/FamilyHQ.Services.Tests/Weather/TemperatureConverterTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class TemperatureConverterTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(20, 68)]
    public void Converts_celsius_to_fahrenheit(double celsius, double expectedFahrenheit)
    {
        var result = TemperatureConverter.Convert(celsius, TemperatureUnit.Fahrenheit);
        result.Should().BeApproximately(expectedFahrenheit, 0.1);
    }

    [Fact]
    public void Celsius_unit_returns_unchanged()
    {
        var result = TemperatureConverter.Convert(20, TemperatureUnit.Celsius);
        result.Should().Be(20);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "TemperatureConverterTests" -v n`
Expected: FAIL — `TemperatureConverter` not found

- [ ] **Step 3: Create TemperatureConverter**

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;

public static class TemperatureConverter
{
    public static double Convert(double celsius, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => Math.Round(celsius * 9.0 / 5.0 + 32, 1),
        _ => celsius
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "TemperatureConverterTests" -v n`
Expected: All tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Services/Weather/TemperatureConverter.cs tests/FamilyHQ.Services.Tests/
git commit -m "feat(weather): add temperature converter with tests"
```

---

### Task 13: OpenMeteoWeatherProvider

**Files:**
- Create: `src/FamilyHQ.Services/Weather/OpenMeteoWeatherProvider.cs`
- Create: `src/FamilyHQ.Services/Weather/OpenMeteoModels.cs`
- Create: `tests/FamilyHQ.Services.Tests/Weather/OpenMeteoWeatherProviderTests.cs`

- [ ] **Step 1: Create Open-Meteo JSON deserialization models**

```csharp
namespace FamilyHQ.Services.Weather;

using System.Text.Json.Serialization;

public record OpenMeteoApiResponse(
    [property: JsonPropertyName("current")] OpenMeteoCurrentData? Current,
    [property: JsonPropertyName("hourly")] OpenMeteoHourlyData? Hourly,
    [property: JsonPropertyName("daily")] OpenMeteoDailyData? Daily);

public record OpenMeteoCurrentData(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("temperature_2m")] double Temperature,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] double WindSpeed);

public record OpenMeteoHourlyData(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("temperature_2m")] List<double> Temperature,
    [property: JsonPropertyName("weather_code")] List<int> WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] List<double> WindSpeed);

public record OpenMeteoDailyData(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("weather_code")] List<int> WeatherCode,
    [property: JsonPropertyName("temperature_2m_max")] List<double> TemperatureMax,
    [property: JsonPropertyName("temperature_2m_min")] List<double> TemperatureMin,
    [property: JsonPropertyName("wind_speed_10m_max")] List<double> WindSpeedMax);
```

- [ ] **Step 2: Write the failing test**

```csharp
namespace FamilyHQ.Services.Tests.Weather;

using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Enums;
using FamilyHQ.Services.Weather;
using FluentAssertions;

public class OpenMeteoWeatherProviderTests
{
    [Fact]
    public async Task Parses_current_weather_from_api_response()
    {
        var json = JsonSerializer.Serialize(new OpenMeteoApiResponse(
            Current: new OpenMeteoCurrentData("2026-04-01T12:00", 14.5, 3, 22.0),
            Hourly: new OpenMeteoHourlyData(
                ["2026-04-01T12:00", "2026-04-01T13:00"],
                [14.5, 15.0],
                [3, 0],
                [22.0, 18.0]),
            Daily: new OpenMeteoDailyData(
                ["2026-04-01", "2026-04-02"],
                [3, 0],
                [16.0, 18.0],
                [8.0, 9.0],
                [25.0, 12.0])));

        var handler = new FakeHttpHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var provider = new OpenMeteoWeatherProvider(httpClient);

        var result = await provider.GetWeatherAsync(53.35, -6.26);

        result.CurrentCondition.Should().Be(WeatherCondition.Cloudy);
        result.CurrentTemperatureCelsius.Should().Be(14.5);
        result.CurrentWindSpeedKmh.Should().Be(22.0);
        result.HourlyForecasts.Should().HaveCount(2);
        result.DailyForecasts.Should().HaveCount(2);
        result.DailyForecasts[0].HighCelsius.Should().Be(16.0);
        result.DailyForecasts[0].LowCelsius.Should().Be(8.0);
    }

    private class FakeHttpHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "OpenMeteoWeatherProviderTests" -v n`
Expected: FAIL — `OpenMeteoWeatherProvider` not found

- [ ] **Step 4: Create OpenMeteoWeatherProvider**

```csharp
namespace FamilyHQ.Services.Weather;

using System.Globalization;
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;

public class OpenMeteoWeatherProvider(HttpClient httpClient) : IWeatherProvider
{
    public async Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var lat = latitude.ToString(CultureInfo.InvariantCulture);
        var lon = longitude.ToString(CultureInfo.InvariantCulture);

        var url = $"/v1/forecast?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,weather_code,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,wind_speed_10m_max"
            + "&forecast_days=16"
            + "&timezone=auto";

        var apiResponse = await httpClient.GetFromJsonAsync<OpenMeteoApiResponse>(url, ct)
            ?? throw new InvalidOperationException("Weather API returned null response.");

        var currentCondition = WeatherCondition.Clear;
        var currentTemp = 0.0;
        var currentWind = 0.0;

        if (apiResponse.Current is not null)
        {
            currentCondition = WmoCodeMapper.ToCondition(apiResponse.Current.WeatherCode);
            currentTemp = apiResponse.Current.Temperature;
            currentWind = apiResponse.Current.WindSpeed;
        }

        var hourly = new List<WeatherHourlyItem>();
        if (apiResponse.Hourly is not null)
        {
            for (var i = 0; i < apiResponse.Hourly.Time.Count; i++)
            {
                hourly.Add(new WeatherHourlyItem(
                    DateTimeOffset.Parse(apiResponse.Hourly.Time[i], CultureInfo.InvariantCulture),
                    WmoCodeMapper.ToCondition(apiResponse.Hourly.WeatherCode[i]),
                    apiResponse.Hourly.Temperature[i],
                    apiResponse.Hourly.WindSpeed[i]));
            }
        }

        var daily = new List<WeatherDailyItem>();
        if (apiResponse.Daily is not null)
        {
            for (var i = 0; i < apiResponse.Daily.Time.Count; i++)
            {
                daily.Add(new WeatherDailyItem(
                    DateOnly.Parse(apiResponse.Daily.Time[i], CultureInfo.InvariantCulture),
                    WmoCodeMapper.ToCondition(apiResponse.Daily.WeatherCode[i]),
                    apiResponse.Daily.TemperatureMax[i],
                    apiResponse.Daily.TemperatureMin[i],
                    apiResponse.Daily.WindSpeedMax[i]));
            }
        }

        return new WeatherResponse(currentCondition, currentTemp, currentWind, hourly, daily);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Services.Tests/ --filter "OpenMeteoWeatherProviderTests" -v n`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Services/Weather/OpenMeteoWeatherProvider.cs src/FamilyHQ.Services/Weather/OpenMeteoModels.cs tests/FamilyHQ.Services.Tests/
git commit -m "feat(weather): add OpenMeteoWeatherProvider with API deserialization and tests"
```

---

### Task 14: IWeatherService and WeatherService

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherService.cs`
- Create: `src/FamilyHQ.Services/Weather/WeatherService.cs`

- [ ] **Step 1: Create IWeatherService interface**

```csharp
namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.DTOs;

public interface IWeatherService
{
    Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default);
    Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct = default);
    Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct = default);
    Task<WeatherSettingDto> GetSettingsAsync(CancellationToken ct = default);
    Task<WeatherSettingDto> UpdateSettingsAsync(WeatherSettingDto dto, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create WeatherService**

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;

public class WeatherService(
    IWeatherDataPointRepository dataPointRepo,
    IWeatherSettingRepository settingRepo,
    ILocationService locationService) : IWeatherService
{
    public async Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var location = await locationService.GetEffectiveLocationAsync(ct);
        if (location is null) return null;

        var setting = await settingRepo.GetOrCreateAsync(ct);
        var locationSetting = await GetLocationSettingIdAsync(ct);
        if (locationSetting is null) return null;

        var current = await dataPointRepo.GetCurrentAsync(locationSetting.Value, ct);
        if (current is null) return null;

        var temp = TemperatureConverter.Convert(current.TemperatureCelsius, setting.TemperatureUnit);

        return new CurrentWeatherDto(
            current.Condition,
            temp,
            current.IsWindy,
            current.WindSpeedKmh,
            WeatherIconMapper.ToIconName(current.Condition));
    }

    public async Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct = default)
    {
        var locationId = await GetLocationSettingIdAsync(ct);
        if (locationId is null) return [];

        var setting = await settingRepo.GetOrCreateAsync(ct);
        var data = await dataPointRepo.GetHourlyAsync(locationId.Value, date, ct);

        return data.Select(d => new HourlyForecastItemDto(
            d.Timestamp,
            d.Condition,
            TemperatureConverter.Convert(d.TemperatureCelsius, setting.TemperatureUnit),
            d.IsWindy,
            WeatherIconMapper.ToIconName(d.Condition))).ToList();
    }

    public async Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct = default)
    {
        var locationId = await GetLocationSettingIdAsync(ct);
        if (locationId is null) return [];

        var setting = await settingRepo.GetOrCreateAsync(ct);
        var data = await dataPointRepo.GetDailyAsync(locationId.Value, days, ct);

        return data.Select(d => new DailyForecastItemDto(
            DateOnly.FromDateTime(d.Timestamp.UtcDateTime),
            d.Condition,
            TemperatureConverter.Convert(d.HighCelsius ?? 0, setting.TemperatureUnit),
            TemperatureConverter.Convert(d.LowCelsius ?? 0, setting.TemperatureUnit),
            d.IsWindy,
            WeatherIconMapper.ToIconName(d.Condition))).ToList();
    }

    public async Task<WeatherSettingDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var setting = await settingRepo.GetOrCreateAsync(ct);
        return new WeatherSettingDto(
            setting.Enabled,
            setting.PollIntervalMinutes,
            setting.TemperatureUnit,
            setting.WindThresholdKmh,
            setting.ApiKey is not null ? "••••••••" : null);
    }

    public async Task<WeatherSettingDto> UpdateSettingsAsync(WeatherSettingDto dto, CancellationToken ct = default)
    {
        var existing = await settingRepo.GetOrCreateAsync(ct);

        existing.Enabled = dto.Enabled;
        existing.PollIntervalMinutes = dto.PollIntervalMinutes;
        existing.TemperatureUnit = dto.TemperatureUnit;
        existing.WindThresholdKmh = dto.WindThresholdKmh;

        if (dto.ApiKey is not null && !dto.ApiKey.Contains('•'))
            existing.ApiKey = dto.ApiKey;

        var updated = await settingRepo.UpsertAsync(existing, ct);

        return new WeatherSettingDto(
            updated.Enabled,
            updated.PollIntervalMinutes,
            updated.TemperatureUnit,
            updated.WindThresholdKmh,
            updated.ApiKey is not null ? "••••••••" : null);
    }

    private async Task<int?> GetLocationSettingIdAsync(CancellationToken ct)
    {
        var location = await locationService.GetEffectiveLocationAsync(ct);
        return location?.LocationSettingId;
    }
}
```

Note: The `GetLocationSettingIdAsync` method assumes `LocationResult` exposes a `LocationSettingId`. Check the existing `LocationResult` record and add the `LocationSettingId` property if it doesn't exist. The weather data is keyed by the DB `LocationSetting.Id`, so the service needs access to it.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.Services/`
Expected: Build succeeded (may require adjusting `LocationResult` — see note above)

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IWeatherService.cs src/FamilyHQ.Services/Weather/WeatherService.cs
git commit -m "feat(weather): add IWeatherService and WeatherService implementation"
```

---

### Task 15: IWeatherBroadcaster and SignalR Broadcaster

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherBroadcaster.cs`
- Create: `src/FamilyHQ.WebApi/Hubs/SignalRWeatherBroadcaster.cs`

- [ ] **Step 1: Create IWeatherBroadcaster interface**

```csharp
namespace FamilyHQ.Core.Interfaces;

public interface IWeatherBroadcaster
{
    Task BroadcastWeatherUpdatedAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create SignalRWeatherBroadcaster**

```csharp
namespace FamilyHQ.WebApi.Hubs;

using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

public class SignalRWeatherBroadcaster(IHubContext<CalendarHub> hubContext) : IWeatherBroadcaster
{
    public Task BroadcastWeatherUpdatedAsync(CancellationToken ct = default) =>
        hubContext.Clients.All.SendAsync("WeatherUpdated", ct);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebApi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IWeatherBroadcaster.cs src/FamilyHQ.WebApi/Hubs/SignalRWeatherBroadcaster.cs
git commit -m "feat(weather): add IWeatherBroadcaster and SignalR implementation"
```

---

### Task 16: WeatherPollerService

**Files:**
- Create: `src/FamilyHQ.Services/Weather/WeatherPollerService.cs`

- [ ] **Step 1: Create WeatherPollerService**

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class WeatherPollerService(
    IServiceProvider serviceProvider,
    IWeatherBroadcaster weatherBroadcaster,
    ILogger<WeatherPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan DisabledCheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPollCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weather poll cycle failed — retrying next interval");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunPollCycleAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var settingRepo = scope.ServiceProvider.GetRequiredService<IWeatherSettingRepository>();
        var setting = await settingRepo.GetOrCreateAsync(ct);

        if (!setting.Enabled)
        {
            logger.LogDebug("Weather polling disabled — checking again in {Interval}", DisabledCheckInterval);
            await Task.Delay(DisabledCheckInterval, ct);
            return;
        }

        var locationService = scope.ServiceProvider.GetRequiredService<ILocationService>();
        var location = await locationService.GetEffectiveLocationAsync(ct);

        if (location is null)
        {
            logger.LogInformation("No location configured — skipping weather poll");
            await Task.Delay(DisabledCheckInterval, ct);
            return;
        }

        var provider = scope.ServiceProvider.GetRequiredService<IWeatherProvider>();
        var response = await provider.GetWeatherAsync(location.Latitude, location.Longitude, ct);

        var dataPoints = MapToDataPoints(response, location.LocationSettingId, setting.WindThresholdKmh);

        var dataPointRepo = scope.ServiceProvider.GetRequiredService<IWeatherDataPointRepository>();
        await dataPointRepo.ReplaceAllAsync(location.LocationSettingId, dataPoints, ct);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(ct);
        logger.LogInformation("Weather data updated — {CurrentCondition}, {Temp}°C, {HourlyCount} hourly, {DailyCount} daily",
            response.CurrentCondition, response.CurrentTemperatureCelsius,
            response.HourlyForecasts.Count, response.DailyForecasts.Count);

        var interval = TimeSpan.FromMinutes(Math.Max(setting.PollIntervalMinutes, 1));
        await Task.Delay(interval, ct);
    }

    private static List<WeatherDataPoint> MapToDataPoints(
        WeatherResponse response, int locationSettingId, double windThreshold)
    {
        var now = DateTimeOffset.UtcNow;
        var points = new List<WeatherDataPoint>();

        // Current
        points.Add(new WeatherDataPoint
        {
            LocationSettingId = locationSettingId,
            Timestamp = now,
            Condition = response.CurrentCondition,
            TemperatureCelsius = response.CurrentTemperatureCelsius,
            WindSpeedKmh = response.CurrentWindSpeedKmh,
            IsWindy = response.CurrentWindSpeedKmh >= windThreshold,
            DataType = WeatherDataType.Current,
            RetrievedAt = now
        });

        // Hourly
        foreach (var h in response.HourlyForecasts)
        {
            points.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = h.Time,
                Condition = h.Condition,
                TemperatureCelsius = h.TemperatureCelsius,
                WindSpeedKmh = h.WindSpeedKmh,
                IsWindy = h.WindSpeedKmh >= windThreshold,
                DataType = WeatherDataType.Hourly,
                RetrievedAt = now
            });
        }

        // Daily
        foreach (var d in response.DailyForecasts)
        {
            points.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = d.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Condition = d.Condition,
                TemperatureCelsius = (d.HighCelsius + d.LowCelsius) / 2,
                HighCelsius = d.HighCelsius,
                LowCelsius = d.LowCelsius,
                WindSpeedKmh = d.WindSpeedMaxKmh,
                IsWindy = d.WindSpeedMaxKmh >= windThreshold,
                DataType = WeatherDataType.Daily,
                RetrievedAt = now
            });
        }

        return points;
    }
}
```

Note: This assumes `LocationResult` has a `LocationSettingId` property. Verify and adjust as needed (see Task 14 note).

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.Services/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.Services/Weather/WeatherPollerService.cs
git commit -m "feat(weather): add WeatherPollerService background poller"
```

---

### Task 17: DI Registration and Configuration

**Files:**
- Modify: `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`
- Modify: `src/FamilyHQ.WebApi/Program.cs`
- Modify: `src/FamilyHQ.WebApi/appsettings.json`
- Modify: `src/FamilyHQ.WebApi/appsettings.Development.json`

- [ ] **Step 1: Add Weather section to appsettings.json**

Add to `src/FamilyHQ.WebApi/appsettings.json`:

```json
"Weather": {
  "BaseUrl": "https://api.open-meteo.com",
  "PollIntervalMinutes": 30,
  "MinPollIntervalMinutes": 1,
  "WindThresholdKmh": 30,
  "Enabled": true
}
```

- [ ] **Step 2: Add Weather section to appsettings.Development.json**

Add to `src/FamilyHQ.WebApi/appsettings.Development.json`:

```json
"Weather": {
  "BaseUrl": "https://localhost:7199",
  "PollIntervalMinutes": 5,
  "MinPollIntervalMinutes": 1,
  "WindThresholdKmh": 30,
  "Enabled": true
}
```

- [ ] **Step 3: Register weather services in ServiceCollectionExtensions**

Add to `src/FamilyHQ.Services/ServiceCollectionExtensions.cs` inside `AddFamilyHqServices`:

```csharp
services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));

services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WeatherOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

services.AddScoped<IWeatherService, WeatherService>();
services.AddHostedService<WeatherPollerService>();
```

Add the required using statements:

```csharp
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Weather;
using Microsoft.Extensions.Options;
```

- [ ] **Step 4: Register SignalRWeatherBroadcaster in WebApi Program.cs**

Add to `src/FamilyHQ.WebApi/Program.cs` near the existing `IThemeBroadcaster` registration:

```csharp
builder.Services.AddSingleton<IWeatherBroadcaster, SignalRWeatherBroadcaster>();
```

Add the using:

```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Hubs;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/FamilyHQ.WebApi/`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Services/ServiceCollectionExtensions.cs src/FamilyHQ.WebApi/Program.cs src/FamilyHQ.WebApi/appsettings.json src/FamilyHQ.WebApi/appsettings.Development.json
git commit -m "feat(weather): register weather services and configure appsettings"
```

---

### Task 18: WeatherController API Endpoints

**Files:**
- Create: `src/FamilyHQ.WebApi/Controllers/WeatherController.cs`
- Modify: `src/FamilyHQ.WebApi/Controllers/SettingsController.cs` (add weather settings endpoints)

- [ ] **Step 1: Create WeatherController**

```csharp
namespace FamilyHQ.WebApi.Controllers;

using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class WeatherController(IWeatherService weatherService, ILogger<WeatherController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var result = await weatherService.GetCurrentAsync(ct);
        if (result is null)
            return NoContent();

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly([FromQuery] string date, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var parsedDate))
            return BadRequest("Invalid date format. Use yyyy-MM-dd.");

        var result = await weatherService.GetHourlyAsync(parsedDate, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast([FromQuery] int days = 5, CancellationToken ct = default)
    {
        if (days is < 1 or > 16)
            return BadRequest("Days must be between 1 and 16.");

        var result = await weatherService.GetDailyForecastAsync(days, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 2: Add weather settings endpoints to SettingsController**

Add these methods to `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`:

```csharp
[HttpGet("weather")]
public async Task<IActionResult> GetWeatherSettings(CancellationToken ct)
{
    var dto = await _weatherService.GetSettingsAsync(ct);
    return Ok(dto);
}

[HttpPut("weather")]
public async Task<IActionResult> UpdateWeatherSettings([FromBody] WeatherSettingDto dto, CancellationToken ct)
{
    var validator = new WeatherSettingDtoValidator();
    var validationResult = await validator.ValidateAsync(dto, ct);
    if (!validationResult.IsValid)
        return BadRequest(validationResult.Errors);

    var updated = await _weatherService.UpdateSettingsAsync(dto, ct);
    return Ok(updated);
}
```

Inject `IWeatherService` in the SettingsController constructor (add it alongside existing dependencies).

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebApi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/WeatherController.cs src/FamilyHQ.WebApi/Controllers/SettingsController.cs
git commit -m "feat(weather): add weather API endpoints and settings endpoints"
```

---

## Phase 4: Simulator Extension

### Task 19: Simulator Weather Models and Database

**Files:**
- Create: `tools/FamilyHQ.Simulator/Models/SimulatedWeather.cs`
- Modify: `tools/FamilyHQ.Simulator/Data/SimContext.cs`

- [ ] **Step 1: Create SimulatedWeather model**

```csharp
namespace FamilyHQ.Simulator.Models;

public class SimulatedWeather
{
    public int Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DataType { get; set; } = "current"; // "current", "hourly", "daily"
    public string Time { get; set; } = "";
    public int WeatherCode { get; set; }
    public double Temperature { get; set; }
    public double? TemperatureMax { get; set; }
    public double? TemperatureMin { get; set; }
    public double WindSpeed { get; set; }
    public double? WindSpeedMax { get; set; }
}
```

- [ ] **Step 2: Add DbSet to SimContext**

Add to `tools/FamilyHQ.Simulator/Data/SimContext.cs`:

```csharp
public DbSet<SimulatedWeather> SimulatedWeather => Set<SimulatedWeather>();
```

- [ ] **Step 3: Generate and apply migration**

```bash
cd tools/FamilyHQ.Simulator
dotnet ef migrations add AddSimulatedWeather
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build tools/FamilyHQ.Simulator/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add tools/FamilyHQ.Simulator/
git commit -m "feat(weather): add SimulatedWeather model and migration to simulator"
```

---

### Task 20: Simulator Backdoor Weather Controller

**Files:**
- Create: `tools/FamilyHQ.Simulator/Controllers/BackdoorWeatherController.cs`

- [ ] **Step 1: Create backdoor controller**

```csharp
namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/simulator/backdoor/weather")]
public class BackdoorWeatherController(SimContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SetWeather([FromBody] SetWeatherRequest request, CancellationToken ct)
    {
        // Remove existing data for this location
        var existing = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - request.Latitude) < 0.001
                     && Math.Abs(w.Longitude - request.Longitude) < 0.001)
            .ToListAsync(ct);

        db.SimulatedWeather.RemoveRange(existing);

        // Add current
        if (request.Current is not null)
        {
            db.SimulatedWeather.Add(new SimulatedWeather
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                DataType = "current",
                Time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
                WeatherCode = request.Current.WeatherCode,
                Temperature = request.Current.Temperature,
                WindSpeed = request.Current.WindSpeed
            });
        }

        // Add hourly
        if (request.Hourly is not null)
        {
            foreach (var h in request.Hourly)
            {
                db.SimulatedWeather.Add(new SimulatedWeather
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    DataType = "hourly",
                    Time = h.Time,
                    WeatherCode = h.WeatherCode,
                    Temperature = h.Temperature,
                    WindSpeed = h.WindSpeed
                });
            }
        }

        // Add daily
        if (request.Daily is not null)
        {
            foreach (var d in request.Daily)
            {
                db.SimulatedWeather.Add(new SimulatedWeather
                {
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    DataType = "daily",
                    Time = d.Date,
                    WeatherCode = d.WeatherCode,
                    TemperatureMax = d.TemperatureMax,
                    TemperatureMin = d.TemperatureMin,
                    WindSpeedMax = d.WindSpeedMax
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Weather data set" });
    }

    [HttpDelete]
    public async Task<IActionResult> ClearWeather(
        [FromQuery] double latitude, [FromQuery] double longitude, CancellationToken ct)
    {
        var existing = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - latitude) < 0.001
                     && Math.Abs(w.Longitude - longitude) < 0.001)
            .ToListAsync(ct);

        db.SimulatedWeather.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Weather data cleared" });
    }
}

public record SetWeatherRequest(
    double Latitude,
    double Longitude,
    SetWeatherCurrentRequest? Current,
    List<SetWeatherHourlyRequest>? Hourly,
    List<SetWeatherDailyRequest>? Daily);

public record SetWeatherCurrentRequest(int WeatherCode, double Temperature, double WindSpeed);
public record SetWeatherHourlyRequest(string Time, int WeatherCode, double Temperature, double WindSpeed);
public record SetWeatherDailyRequest(string Date, int WeatherCode, double TemperatureMax, double TemperatureMin, double WindSpeedMax);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tools/FamilyHQ.Simulator/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add tools/FamilyHQ.Simulator/Controllers/BackdoorWeatherController.cs
git commit -m "feat(weather): add simulator backdoor weather controller"
```

---

### Task 21: Simulator Open-Meteo Compatible Forecast Endpoint

**Files:**
- Create: `tools/FamilyHQ.Simulator/Controllers/ForecastController.cs`

- [ ] **Step 1: Create forecast endpoint mimicking Open-Meteo**

```csharp
namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("v1")]
public class ForecastController(SimContext db) : ControllerBase
{
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        CancellationToken ct)
    {
        var data = await db.SimulatedWeather
            .Where(w => Math.Abs(w.Latitude - latitude) < 0.001
                     && Math.Abs(w.Longitude - longitude) < 0.001)
            .ToListAsync(ct);

        var current = data.FirstOrDefault(d => d.DataType == "current");
        var hourly = data.Where(d => d.DataType == "hourly").OrderBy(d => d.Time).ToList();
        var daily = data.Where(d => d.DataType == "daily").OrderBy(d => d.Time).ToList();

        var response = new
        {
            current = current is not null ? new
            {
                time = current.Time,
                temperature_2m = current.Temperature,
                weather_code = current.WeatherCode,
                wind_speed_10m = current.WindSpeed
            } : new
            {
                time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm"),
                temperature_2m = 15.0,
                weather_code = 0,
                wind_speed_10m = 5.0
            },
            hourly = new
            {
                time = hourly.Select(h => h.Time).ToList(),
                temperature_2m = hourly.Select(h => h.Temperature).ToList(),
                weather_code = hourly.Select(h => h.WeatherCode).ToList(),
                wind_speed_10m = hourly.Select(h => h.WindSpeed).ToList()
            },
            daily = new
            {
                time = daily.Select(d => d.Time).ToList(),
                weather_code = daily.Select(d => d.WeatherCode).ToList(),
                temperature_2m_max = daily.Select(d => d.TemperatureMax ?? 15.0).ToList(),
                temperature_2m_min = daily.Select(d => d.TemperatureMin ?? 5.0).ToList(),
                wind_speed_10m_max = daily.Select(d => d.WindSpeedMax ?? 10.0).ToList()
            }
        };

        return Ok(response);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build tools/FamilyHQ.Simulator/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add tools/FamilyHQ.Simulator/Controllers/ForecastController.cs
git commit -m "feat(weather): add simulator Open-Meteo compatible forecast endpoint"
```

---

## Phase 5: Frontend — Services

### Task 22: SignalR WeatherUpdated Subscription

**Files:**
- Modify: `src/FamilyHQ.WebUi/Services/SignalRService.cs`

- [ ] **Step 1: Add WeatherUpdated event to SignalRService**

Add to the `SignalRService` class:

Event declaration (alongside existing events):
```csharp
public event Action? OnWeatherUpdated;
```

In the constructor (alongside existing `_hubConnection.On` registrations):
```csharp
_hubConnection.On("WeatherUpdated", () => OnWeatherUpdated?.Invoke());
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/SignalRService.cs
git commit -m "feat(weather): add WeatherUpdated event to SignalRService"
```

---

### Task 23: Frontend WeatherService

**Files:**
- Create: `src/FamilyHQ.WebUi/Services/IWeatherUiService.cs`
- Create: `src/FamilyHQ.WebUi/Services/WeatherUiService.cs`
- Modify: `src/FamilyHQ.WebUi/Program.cs`
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`

- [ ] **Step 1: Create IWeatherUiService interface**

```csharp
namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.DTOs;

public interface IWeatherUiService : IAsyncDisposable
{
    CurrentWeatherDto? CurrentWeather { get; }
    List<DailyForecastItemDto> DailyForecast { get; }
    List<HourlyForecastItemDto> HourlyForecast { get; }
    WeatherSettingDto? Settings { get; }
    bool IsEnabled { get; }

    event Action? OnWeatherChanged;

    Task InitialiseAsync();
    Task RefreshAsync();
    Task LoadHourlyAsync(DateOnly date);
    Task LoadDailyAsync(int days);
    Task<WeatherSettingDto> LoadSettingsAsync();
    Task<WeatherSettingDto> SaveSettingsAsync(WeatherSettingDto dto);
}
```

- [ ] **Step 2: Create WeatherUiService**

```csharp
namespace FamilyHQ.WebUi.Services;

using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

public class WeatherUiService : IWeatherUiService
{
    private readonly HttpClient _httpClient;
    private readonly SignalRService _signalRService;
    private readonly Action _weatherUpdatedHandler;

    public CurrentWeatherDto? CurrentWeather { get; private set; }
    public List<DailyForecastItemDto> DailyForecast { get; private set; } = [];
    public List<HourlyForecastItemDto> HourlyForecast { get; private set; } = [];
    public WeatherSettingDto? Settings { get; private set; }
    public bool IsEnabled => Settings?.Enabled ?? true;

    public event Action? OnWeatherChanged;

    public WeatherUiService(HttpClient httpClient, SignalRService signalRService)
    {
        _httpClient = httpClient;
        _signalRService = signalRService;

        _weatherUpdatedHandler = () => _ = RefreshAsync();
        _signalRService.OnWeatherUpdated += _weatherUpdatedHandler;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");

            if (Settings?.Enabled != true) return;

            CurrentWeather = await _httpClient.GetFromJsonAsync<CurrentWeatherDto>("api/weather/current");
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>("api/weather/forecast?days=14") ?? [];
        }
        catch (HttpRequestException)
        {
            // Non-critical — weather may not be available yet
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");

            if (Settings?.Enabled != true)
            {
                CurrentWeather = null;
                DailyForecast = [];
                HourlyForecast = [];
                OnWeatherChanged?.Invoke();
                return;
            }

            CurrentWeather = await _httpClient.GetFromJsonAsync<CurrentWeatherDto>("api/weather/current");
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>("api/weather/forecast?days=14") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException)
        {
            // Retain last known data
        }
    }

    public async Task LoadHourlyAsync(DateOnly date)
    {
        try
        {
            HourlyForecast = await _httpClient.GetFromJsonAsync<List<HourlyForecastItemDto>>(
                $"api/weather/hourly?date={date:yyyy-MM-dd}") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException)
        {
            // Retain last known data
        }
    }

    public async Task LoadDailyAsync(int days)
    {
        try
        {
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>(
                $"api/weather/forecast?days={days}") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException)
        {
            // Retain last known data
        }
    }

    public async Task<WeatherSettingDto> LoadSettingsAsync()
    {
        Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");
        return Settings!;
    }

    public async Task<WeatherSettingDto> SaveSettingsAsync(WeatherSettingDto dto)
    {
        var response = await _httpClient.PutAsJsonAsync("api/settings/weather", dto);
        response.EnsureSuccessStatusCode();
        Settings = await response.Content.ReadFromJsonAsync<WeatherSettingDto>();
        OnWeatherChanged?.Invoke();
        return Settings!;
    }

    public ValueTask DisposeAsync()
    {
        _signalRService.OnWeatherUpdated -= _weatherUpdatedHandler;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: Register in DI (Program.cs)**

Add to `src/FamilyHQ.WebUi/Program.cs` alongside existing service registrations:

```csharp
builder.Services.AddScoped<IWeatherUiService, WeatherUiService>();
```

- [ ] **Step 4: Initialise in MainLayout**

Add to `src/FamilyHQ.WebUi/Layout/MainLayout.razor`:

Inject:
```razor
@inject IWeatherUiService WeatherService
```

In `OnInitializedAsync`, add after existing initialisations:
```csharp
await WeatherService.InitialiseAsync();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/IWeatherUiService.cs src/FamilyHQ.WebUi/Services/WeatherUiService.cs src/FamilyHQ.WebUi/Program.cs src/FamilyHQ.WebUi/Layout/MainLayout.razor
git commit -m "feat(weather): add frontend WeatherUiService with SignalR subscription"
```

---

## Phase 6: Frontend — Components

### Task 24: Weather SVG Icons

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Weather/WeatherIcon.razor`

- [ ] **Step 1: Create WeatherIcon component**

This component renders the appropriate SVG based on the icon name and optional wind modifier.

```razor
@namespace FamilyHQ.WebUi.Components.Weather

<span class="weather-icon @(IsWindy ? "weather-icon--windy" : "")">
    @switch (IconName)
    {
        case "clear":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="5"/>
                <line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/>
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
                <line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/>
                <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
            </svg>
            break;
        case "partly-cloudy":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="17" cy="7" r="4"/>
                <line x1="17" y1="1" x2="17" y2="2"/><line x1="23" y1="7" x2="22" y2="7"/>
                <line x1="21.24" y1="2.76" x2="20.54" y2="3.46"/>
                <path d="M6 19a4 4 0 0 1 0-8h1a5 5 0 0 1 9.9-1H18a3 3 0 0 1 0 6H6z"/>
            </svg>
            break;
        case "cloudy":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
            </svg>
            break;
        case "fog":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z" opacity="0.4"/>
                <line x1="3" y1="20" x2="21" y2="20"/><line x1="3" y1="17" x2="21" y2="17"/>
                <line x1="3" y1="14" x2="21" y2="14"/>
            </svg>
            break;
        case "drizzle":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <line x1="8" y1="19" x2="8" y2="21" stroke-dasharray="1 3"/>
                <line x1="16" y1="19" x2="16" y2="21" stroke-dasharray="1 3"/>
            </svg>
            break;
        case "light-rain":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <line x1="8" y1="19" x2="7" y2="22"/><line x1="12" y1="19" x2="11" y2="22"/>
                <line x1="16" y1="19" x2="15" y2="22"/>
            </svg>
            break;
        case "heavy-rain":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <line x1="7" y1="19" x2="5" y2="23"/><line x1="10" y1="19" x2="8" y2="23"/>
                <line x1="13" y1="19" x2="11" y2="23"/><line x1="16" y1="19" x2="14" y2="23"/>
                <line x1="19" y1="19" x2="17" y2="23"/>
            </svg>
            break;
        case "thunder":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <polyline points="13,16 11,20 15,20 12,24" stroke-width="2.5"/>
            </svg>
            break;
        case "snow":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <circle cx="8" cy="21" r="1" fill="currentColor"/><circle cx="12" cy="22" r="1" fill="currentColor"/>
                <circle cx="16" cy="21" r="1" fill="currentColor"/>
            </svg>
            break;
        case "sleet":
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"/>
                <line x1="8" y1="19" x2="7" y2="22"/>
                <circle cx="12" cy="21" r="1" fill="currentColor"/>
                <line x1="16" y1="19" x2="15" y2="22"/>
            </svg>
            break;
        default:
            <svg viewBox="0 0 24 24" width="@Size" height="@Size" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="5"/>
            </svg>
            break;
    }
    @if (IsWindy)
    {
        <svg class="weather-icon__wind" viewBox="0 0 24 24" width="@(Size * 0.6)" height="@(Size * 0.6)" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M9.59 4.59A2 2 0 1 1 11 8H2"/>
            <path d="M12.59 19.41A2 2 0 1 0 14 16H2"/>
        </svg>
    }
</span>

@code {
    [Parameter] public string IconName { get; set; } = "clear";
    [Parameter] public bool IsWindy { get; set; }
    [Parameter] public int Size { get; set; } = 20;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Weather/WeatherIcon.razor
git commit -m "feat(weather): add WeatherIcon SVG component"
```

---

### Task 25: WeatherStrip Component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Weather/WeatherStrip.razor`
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css` (add weather strip styles)

- [ ] **Step 1: Create WeatherStrip component**

```razor
@namespace FamilyHQ.WebUi.Components.Weather
@using FamilyHQ.WebUi.Services
@implements IDisposable
@inject IWeatherUiService WeatherService

@if (WeatherService.IsEnabled && WeatherService.CurrentWeather is not null)
{
    <div class="weather-strip glass-surface">
        <div class="weather-strip__current">
            <WeatherIcon IconName="@WeatherService.CurrentWeather.IconName"
                         IsWindy="@WeatherService.CurrentWeather.IsWindy"
                         Size="22" />
            <span class="weather-strip__temp">@FormatTemp(WeatherService.CurrentWeather.Temperature)</span>
            <span class="weather-strip__condition">@FormatCondition(WeatherService.CurrentWeather.Condition)</span>
        </div>

        @if (ActiveView == "Month" && ForecastDays.Count > 0)
        {
            <div class="weather-strip__divider"></div>
            <div class="weather-strip__forecast">
                @foreach (var day in ForecastDays.Take(5))
                {
                    <div class="weather-strip__forecast-day">
                        <span class="weather-strip__day-name">@day.Date.ToString("ddd")</span>
                        <WeatherIcon IconName="@day.IconName" IsWindy="@day.IsWindy" Size="16" />
                        <span class="weather-strip__day-temps">
                            <strong>@FormatTemp(day.High)</strong>/@FormatTemp(day.Low)
                        </span>
                    </div>
                }
            </div>
        }
        else if (ActiveView == "Day" && TodayForecast is not null)
        {
            <div class="weather-strip__divider"></div>
            <div class="weather-strip__today-summary">
                <span><strong>@FormatTemp(TodayForecast.High)</strong>/@FormatTemp(TodayForecast.Low)</span>
                <span class="weather-strip__condition">@FormatCondition(TodayForecast.Condition)</span>
            </div>
        }
        else if (ActiveView == "Agenda" && ForecastDays.Count > 0)
        {
            <div class="weather-strip__divider"></div>
            <div class="weather-strip__forecast">
                @foreach (var day in ForecastDays.Take(3))
                {
                    <div class="weather-strip__forecast-day">
                        <span class="weather-strip__day-name">@day.Date.ToString("ddd")</span>
                        <WeatherIcon IconName="@day.IconName" IsWindy="@day.IsWindy" Size="16" />
                        <span class="weather-strip__day-temps">
                            <strong>@FormatTemp(day.High)</strong>/@FormatTemp(day.Low)
                        </span>
                    </div>
                }
            </div>
        }
    </div>
}

@code {
    [Parameter] public string ActiveView { get; set; } = "Month";

    private List<FamilyHQ.Core.DTOs.DailyForecastItemDto> ForecastDays => WeatherService.DailyForecast;

    private FamilyHQ.Core.DTOs.DailyForecastItemDto? TodayForecast =>
        ForecastDays.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(DateTime.Today));

    protected override void OnInitialized()
    {
        WeatherService.OnWeatherChanged += HandleWeatherChanged;
    }

    private void HandleWeatherChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    private static string FormatTemp(double temp) => $"{temp:F0}°";

    private static string FormatCondition(FamilyHQ.Core.Enums.WeatherCondition condition) => condition switch
    {
        FamilyHQ.Core.Enums.WeatherCondition.PartlyCloudy => "Partly Cloudy",
        FamilyHQ.Core.Enums.WeatherCondition.LightRain => "Light Rain",
        FamilyHQ.Core.Enums.WeatherCondition.HeavyRain => "Heavy Rain",
        _ => condition.ToString()
    };

    public void Dispose()
    {
        WeatherService.OnWeatherChanged -= HandleWeatherChanged;
    }
}
```

- [ ] **Step 2: Add weather strip CSS to app.css**

Add to `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* Weather Strip */
.weather-strip {
    display: flex;
    align-items: center;
    padding: 6px 16px;
    gap: 12px;
    font-size: 13px;
    border-radius: 0;
    transition: opacity 0.3s ease;
}

.weather-strip__current {
    display: flex;
    align-items: center;
    gap: 8px;
    flex-shrink: 0;
}

.weather-strip__temp {
    font-weight: 500;
}

.weather-strip__condition {
    opacity: 0.7;
    font-size: 12px;
}

.weather-strip__divider {
    width: 1px;
    height: 20px;
    background: var(--theme-glass-divider);
    flex-shrink: 0;
}

.weather-strip__forecast {
    display: flex;
    gap: 16px;
    overflow: hidden;
}

.weather-strip__forecast-day {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 2px;
    font-size: 11px;
}

.weather-strip__day-name {
    opacity: 0.6;
}

.weather-strip__day-temps {
    font-size: 11px;
}

.weather-strip__today-summary {
    display: flex;
    align-items: center;
    gap: 8px;
}

/* Weather Icon */
.weather-icon {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    color: var(--theme-text);
}

.weather-icon svg {
    vertical-align: middle;
}

.weather-icon__wind {
    opacity: 0.6;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Weather/WeatherStrip.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(weather): add WeatherStrip component and CSS styles"
```

---

### Task 26: Integrate WeatherStrip into Dashboard

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Add WeatherStrip to the dashboard page**

Add the `@using` directive at the top of `Index.razor`:
```razor
@using FamilyHQ.WebUi.Components.Weather
```

Add the `WeatherStrip` component immediately after the `DashboardHeader` in the markup, passing the active view:
```razor
<WeatherStrip ActiveView="@activeView" />
```

Where `activeView` is the string name of the current tab ("Month", "Day", "Agenda"). Check the existing code for how tabs are tracked and use that variable name.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat(weather): integrate WeatherStrip into dashboard"
```

---

### Task 27: Agenda View — Weather in Date Cells

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor`

- [ ] **Step 1: Add weather data to agenda date cells**

Inject the weather service:
```razor
@using FamilyHQ.WebUi.Components.Weather
@inject IWeatherUiService WeatherService
```

In each date cell (leftmost column), after the existing date display, add weather info if available:

```razor
@{
    var dayForecast = WeatherService.DailyForecast
        .FirstOrDefault(f => f.Date == DateOnly.FromDateTime(dayDate));
}
@if (dayForecast is not null && WeatherService.IsEnabled)
{
    <div class="agenda-weather">
        <WeatherIcon IconName="@dayForecast.IconName" IsWindy="@dayForecast.IsWindy" Size="14" />
        <span class="agenda-weather__temps">
            <strong>@($"{dayForecast.High:F0}°")</strong>/@($"{dayForecast.Low:F0}°")
        </span>
    </div>
}
```

Subscribe to weather changes so the component re-renders:
```csharp
protected override void OnInitialized()
{
    WeatherService.OnWeatherChanged += HandleWeatherChanged;
    // ... existing code
}

private void HandleWeatherChanged() => InvokeAsync(StateHasChanged);
```

Unsubscribe in disposal.

- [ ] **Step 2: Add agenda weather CSS**

Add to `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* Agenda Weather */
.agenda-weather {
    display: flex;
    align-items: center;
    gap: 4px;
    margin-top: 4px;
}

.agenda-weather__temps {
    font-size: 11px;
    color: var(--theme-text-muted);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(weather): add weather icons and temps to agenda view date cells"
```

---

### Task 28: Day View — Hourly Weather

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor`

- [ ] **Step 1: Add hourly weather to day view time column**

Inject the weather service:
```razor
@using FamilyHQ.WebUi.Components.Weather
@inject IWeatherUiService WeatherService
```

When the day changes (in the existing day navigation logic), load hourly data:
```csharp
await WeatherService.LoadHourlyAsync(DateOnly.FromDateTime(selectedDate));
```

In each hour row, after the time label, add weather info:

```razor
@{
    var hourWeather = WeatherService.HourlyForecast
        .FirstOrDefault(h => h.Hour.Hour == hour);
}
@if (hourWeather is not null && WeatherService.IsEnabled)
{
    <WeatherIcon IconName="@hourWeather.IconName" IsWindy="@hourWeather.IsWindy" Size="14" />
    <span class="day-hour-temp">@($"{hourWeather.Temperature:F0}°")</span>
}
```

Subscribe to weather changes and unsubscribe on disposal.

- [ ] **Step 2: Add day view weather CSS**

Add to `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* Day View Hourly Weather */
.day-hour-temp {
    font-size: 12px;
    opacity: 0.8;
    color: var(--theme-text-muted);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(weather): add hourly weather inline in day view time column"
```

---

### Task 29: Weather Overlay — CSS Animations

**Files:**
- Create: `src/FamilyHQ.WebUi/wwwroot/js/weather.js`
- Create: `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor`
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css` (add overlay animations)

- [ ] **Step 1: Create weather.js for overlay management**

```javascript
export function setWeatherOverlay(condition, isWindy) {
    const overlay = document.getElementById('weather-overlay');
    if (!overlay) return;

    // Clear existing classes
    overlay.className = '';

    if (!condition || condition === 'Clear') {
        overlay.innerHTML = '';
        return;
    }

    overlay.className = `weather-${condition.toLowerCase()}`;
    if (isWindy) {
        overlay.classList.add('weather-windy');
    }
}

export function clearWeatherOverlay() {
    const overlay = document.getElementById('weather-overlay');
    if (!overlay) return;
    overlay.className = '';
    overlay.innerHTML = '';
}
```

- [ ] **Step 2: Create WeatherOverlay component**

```razor
@namespace FamilyHQ.WebUi.Components.Weather
@using FamilyHQ.WebUi.Services
@implements IAsyncDisposable
@inject IWeatherUiService WeatherService
@inject IJSRuntime JsRuntime

@code {
    private IJSObjectReference? _module;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/weather.js");
            WeatherService.OnWeatherChanged += HandleWeatherChanged;
            await UpdateOverlay();
        }
    }

    private async void HandleWeatherChanged()
    {
        await InvokeAsync(async () => await UpdateOverlay());
    }

    private async Task UpdateOverlay()
    {
        if (_module is null) return;

        if (!WeatherService.IsEnabled || WeatherService.CurrentWeather is null)
        {
            await _module.InvokeVoidAsync("clearWeatherOverlay");
            return;
        }

        var condition = WeatherService.CurrentWeather.Condition.ToString();
        var isWindy = WeatherService.CurrentWeather.IsWindy;
        await _module.InvokeVoidAsync("setWeatherOverlay", condition, isWindy);
    }

    public async ValueTask DisposeAsync()
    {
        WeatherService.OnWeatherChanged -= HandleWeatherChanged;
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("clearWeatherOverlay");
            await _module.DisposeAsync();
        }
    }
}
```

- [ ] **Step 3: Add weather overlay CSS animations to app.css**

Add to `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* ── Weather Overlay Animations ── */
#weather-overlay {
    position: fixed;
    inset: 0;
    z-index: 1;
    pointer-events: none;
    overflow: hidden;
    transition: opacity 1s ease;
}

/* Rain — falling lines using repeating gradient */
.weather-lightrain::before,
.weather-heavyrain::before,
.weather-drizzle::before {
    content: '';
    position: absolute;
    inset: -100% 0 0 0;
    background: repeating-linear-gradient(
        transparent,
        transparent 98%,
        rgba(174, 194, 224, 0.3) 98%,
        rgba(174, 194, 224, 0.3) 100%
    );
    background-size: 3px 80px;
    animation: weather-rain 0.8s linear infinite;
}

.weather-drizzle::before {
    background-size: 5px 120px;
    animation-duration: 1.5s;
    opacity: 0.5;
}

.weather-heavyrain::before {
    background-size: 2px 60px;
    animation-duration: 0.5s;
    opacity: 0.8;
}

@keyframes weather-rain {
    0% { transform: translateY(-80px); }
    100% { transform: translateY(80px); }
}

/* Thunder — rain + lightning flash */
.weather-thunder::before {
    content: '';
    position: absolute;
    inset: -100% 0 0 0;
    background: repeating-linear-gradient(
        transparent,
        transparent 98%,
        rgba(174, 194, 224, 0.3) 98%,
        rgba(174, 194, 224, 0.3) 100%
    );
    background-size: 2px 60px;
    animation: weather-rain 0.5s linear infinite;
    opacity: 0.8;
}

.weather-thunder::after {
    content: '';
    position: absolute;
    inset: 0;
    background: rgba(255, 255, 255, 0);
    animation: weather-lightning 4s ease-in-out infinite;
}

@keyframes weather-lightning {
    0%, 92%, 94%, 96%, 100% { background: rgba(255, 255, 255, 0); }
    93% { background: rgba(255, 255, 255, 0.15); }
    95% { background: rgba(255, 255, 255, 0.1); }
}

/* Snow — falling dots */
.weather-snow::before {
    content: '';
    position: absolute;
    inset: -100% 0 0 0;
    background-image:
        radial-gradient(2px 2px at 20% 30%, rgba(255,255,255,0.5), transparent),
        radial-gradient(2px 2px at 50% 60%, rgba(255,255,255,0.4), transparent),
        radial-gradient(2px 2px at 80% 20%, rgba(255,255,255,0.5), transparent),
        radial-gradient(3px 3px at 35% 80%, rgba(255,255,255,0.3), transparent),
        radial-gradient(2px 2px at 65% 45%, rgba(255,255,255,0.4), transparent);
    background-size: 100px 100px;
    animation: weather-snow 3s linear infinite;
}

@keyframes weather-snow {
    0% { transform: translateY(-100px) translateX(0); }
    100% { transform: translateY(100px) translateX(20px); }
}

/* Sleet — mix of rain and snow */
.weather-sleet::before {
    content: '';
    position: absolute;
    inset: -100% 0 0 0;
    background:
        repeating-linear-gradient(transparent, transparent 98%, rgba(174,194,224,0.2) 98%, rgba(174,194,224,0.2) 100%),
        radial-gradient(2px 2px at 50% 50%, rgba(255,255,255,0.3), transparent);
    background-size: 4px 80px, 80px 80px;
    animation: weather-rain 1s linear infinite;
}

/* Clouds — drifting opacity */
.weather-partlycloudy::before,
.weather-cloudy::before {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(
        90deg,
        transparent 0%,
        rgba(128,128,128,0.05) 20%,
        rgba(128,128,128,0.08) 40%,
        transparent 60%,
        rgba(128,128,128,0.06) 80%,
        transparent 100%
    );
    animation: weather-clouds 20s ease-in-out infinite;
}

.weather-cloudy::before {
    background: linear-gradient(
        90deg,
        rgba(128,128,128,0.05) 0%,
        rgba(128,128,128,0.12) 25%,
        rgba(128,128,128,0.15) 50%,
        rgba(128,128,128,0.1) 75%,
        rgba(128,128,128,0.05) 100%
    );
    animation-duration: 15s;
}

@keyframes weather-clouds {
    0%, 100% { transform: translateX(-10%); opacity: 0.7; }
    50% { transform: translateX(10%); opacity: 1; }
}

/* Fog */
.weather-fog::before {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(
        180deg,
        transparent 0%,
        rgba(200,200,200,0.08) 30%,
        rgba(200,200,200,0.12) 60%,
        rgba(200,200,200,0.08) 100%
    );
    animation: weather-fog-pulse 6s ease-in-out infinite;
}

@keyframes weather-fog-pulse {
    0%, 100% { opacity: 0.6; }
    50% { opacity: 1; }
}

/* Wind modifier — tilts particle animations */
.weather-windy::before {
    transform-origin: top center;
}

.weather-windy.weather-lightrain::before,
.weather-windy.weather-heavyrain::before,
.weather-windy.weather-drizzle::before,
.weather-windy.weather-thunder::before {
    animation-name: weather-rain-windy;
}

.weather-windy.weather-snow::before {
    animation-name: weather-snow-windy;
}

@keyframes weather-rain-windy {
    0% { transform: translateY(-80px) rotate(15deg); }
    100% { transform: translateY(80px) rotate(15deg); }
}

@keyframes weather-snow-windy {
    0% { transform: translateY(-100px) translateX(0) rotate(10deg); }
    100% { transform: translateY(100px) translateX(40px) rotate(10deg); }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/js/weather.js src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(weather): add weather overlay CSS animations and component"
```

---

### Task 30: Integrate Weather Overlay into Layout

**Files:**
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`

- [ ] **Step 1: Add WeatherOverlay to MainLayout**

Add the using directive:
```razor
@using FamilyHQ.WebUi.Components.Weather
```

Add the `WeatherOverlay` component inside the `<div class="page">`, before `@Body`:
```razor
<WeatherOverlay />
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Layout/MainLayout.razor
git commit -m "feat(weather): integrate weather overlay into main layout"
```

---

## Phase 7: Settings UI

### Task 31: Weather Settings Sub-Page

**Files:**
- Create: `src/FamilyHQ.WebUi/Pages/WeatherSettings.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor` (add link to weather settings)

- [ ] **Step 1: Create WeatherSettings page**

```razor
@page "/settings/weather"
@using FamilyHQ.Core.DTOs
@using FamilyHQ.Core.Enums
@using FamilyHQ.WebUi.Components.Dashboard
@using FamilyHQ.WebUi.Services
@inject IWeatherUiService WeatherService

<DashboardHeader BackUrl="/settings" />

<div class="settings-page">
    <div class="settings-section glass-surface">
        <h2 class="settings-section__title">Weather</h2>

        @if (_isLoading)
        {
            <div class="spinner"></div>
        }
        else if (_settings is not null)
        {
            <div class="form-group">
                <label class="form-label">Enable Weather</label>
                <label class="toggle">
                    <input type="checkbox" @bind="_settings.Enabled" @bind:after="HandleSettingChanged" />
                    <span class="toggle__slider"></span>
                </label>
            </div>

            <div class="form-group">
                <label class="form-label">Temperature Unit</label>
                <select class="form-select" @bind="_temperatureUnit" @bind:after="HandleSettingChanged">
                    <option value="@TemperatureUnit.Celsius">Celsius (°C)</option>
                    <option value="@TemperatureUnit.Fahrenheit">Fahrenheit (°F)</option>
                </select>
            </div>

            <div class="form-group">
                <label class="form-label">Poll Interval (minutes)</label>
                <input type="number" class="form-input" min="1" @bind="_settings.PollIntervalMinutes"
                       @bind:after="HandleSettingChanged" />
            </div>

            <div class="form-group">
                <label class="form-label">Wind Threshold (km/h)</label>
                <input type="number" class="form-input" min="1" step="5" @bind="_settings.WindThresholdKmh"
                       @bind:after="HandleSettingChanged" />
            </div>

            @if (_hasChanges)
            {
                <div class="flex justify-end gap-2 mt-3">
                    <button class="btn-secondary" @onclick="ResetChanges">Cancel</button>
                    <button class="btn-primary" @onclick="SaveSettings" disabled="@_isSaving">
                        @(_isSaving ? "Saving..." : "Save")
                    </button>
                </div>
            }

            @if (_errorMessage is not null)
            {
                <div class="alert-error mt-2">@_errorMessage</div>
            }

            @if (_successMessage is not null)
            {
                <div class="alert-success mt-2">@_successMessage</div>
            }
        }
    </div>
</div>

@code {
    private WeatherSettingDto? _settings;
    private WeatherSettingDto? _original;
    private TemperatureUnit _temperatureUnit;
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _hasChanges;
    private string? _errorMessage;
    private string? _successMessage;

    protected override async Task OnInitializedAsync()
    {
        _settings = await WeatherService.LoadSettingsAsync();
        _original = _settings;
        _temperatureUnit = _settings.TemperatureUnit;
        _isLoading = false;
    }

    private void HandleSettingChanged()
    {
        _settings = _settings! with { TemperatureUnit = _temperatureUnit };
        _hasChanges = _settings != _original;
        _errorMessage = null;
        _successMessage = null;
    }

    private void ResetChanges()
    {
        _settings = _original;
        _temperatureUnit = _original!.TemperatureUnit;
        _hasChanges = false;
        _errorMessage = null;
        _successMessage = null;
    }

    private async Task SaveSettings()
    {
        _isSaving = true;
        _errorMessage = null;
        _successMessage = null;

        try
        {
            _settings = _settings! with { TemperatureUnit = _temperatureUnit };
            var result = await WeatherService.SaveSettingsAsync(_settings);
            _settings = result;
            _original = result;
            _temperatureUnit = result.TemperatureUnit;
            _hasChanges = false;
            _successMessage = "Weather settings saved.";
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to save settings: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }
}
```

Note: The `WeatherSettingDto` is a record with init-only properties. The form bindings above use `@bind` which requires mutable properties. You may need to convert the DTO to a mutable view model class for the form, or use a local class with mutable properties that maps to/from the DTO. Adjust accordingly based on what the existing Settings page does for its bindings.

- [ ] **Step 2: Add weather settings link to Settings page**

Add to `src/FamilyHQ.WebUi/Pages/Settings.razor`, between the Display section and Today's Theme Schedule section:

```razor
<div class="settings-section glass-surface">
    <a href="/settings/weather" class="settings-section__link">
        <h2 class="settings-section__title">Weather</h2>
        <span class="settings-section__arrow">→</span>
    </a>
</div>
```

Add link CSS if not already present:

```css
.settings-section__link {
    display: flex;
    justify-content: space-between;
    align-items: center;
    text-decoration: none;
    color: var(--theme-text);
}

.settings-section__arrow {
    font-size: 18px;
    opacity: 0.5;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/FamilyHQ.WebUi/`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/WeatherSettings.razor src/FamilyHQ.WebUi/Pages/Settings.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(weather): add weather settings sub-page and navigation link"
```

---

## Phase 8: Documentation Update

### Task 32: Update Architecture and UI Design System Docs

**Files:**
- Modify: `.agent/docs/architecture.md`
- Modify: `.agent/docs/ui-design-system.md`

- [ ] **Step 1: Update architecture.md**

Add to Key Entities section:
```markdown
- **WeatherDataPoint**: Stores weather data (current, hourly, daily) for a location. Keyed by LocationSettingId + DataType + Timestamp.
- **WeatherSetting**: Stores weather preferences (enabled, poll interval, temperature unit, wind threshold). Single row.
```

Add to Key Services section:
```markdown
- **IWeatherProvider / OpenMeteoWeatherProvider**: Fetches weather data from Open-Meteo (or simulator). Base URL from config — same code in all environments.
- **IWeatherService / WeatherService**: Reads stored weather data, applies temperature conversion, serves DTOs.
- **WeatherPollerService** (IHostedService): Background poller that fetches weather data at configurable intervals and broadcasts `WeatherUpdated` via SignalR.
```

Add to API Endpoints section:
```markdown
- `GET  /api/weather/current` → CurrentWeatherDto (condition, temperature, wind)
- `GET  /api/weather/hourly?date=yyyy-MM-dd` → List<HourlyForecastItemDto>
- `GET  /api/weather/forecast?days=5` → List<DailyForecastItemDto>
- `GET  /api/settings/weather` → WeatherSettingDto
- `PUT  /api/settings/weather` → upserts weather settings
```

Add to SignalR section:
```markdown
- **WeatherUpdated**: pushed by WeatherPollerService when new weather data is stored. No parameters — UI fetches fresh data via HTTP.
```

Add to Pages & Navigation section:
```markdown
- `/settings/weather` — Weather settings sub-page (enable/disable, temperature unit, poll interval, wind threshold)
```

- [ ] **Step 2: Update ui-design-system.md**

Update the Weather Overlay section to reflect the implementation:
```markdown
## Weather Overlay — Active Implementation

The `#weather-overlay` div renders CSS-only animations based on current weather conditions. Managed by `WeatherOverlay.razor` via JS interop (`weather.js`).

**CSS classes applied to `#weather-overlay`:**
- `.weather-clear` (none — empty overlay)
- `.weather-partlycloudy`, `.weather-cloudy` — drifting cloud opacity
- `.weather-fog` — gradient overlay with opacity pulse
- `.weather-drizzle`, `.weather-lightrain`, `.weather-heavyrain` — falling line animations
- `.weather-thunder` — heavy rain + lightning flash
- `.weather-snow` — falling dot animations
- `.weather-sleet` — mixed rain/snow
- `.weather-windy` (modifier) — tilts particle angle 15°, increases drift

All animations use only `transform` and `opacity` — safe for Pi 3B+.
```

Add Weather Strip to the Settings Page Layout section:
```markdown
5. **Weather** — link to `/settings/weather` sub-page (enable/disable, temperature unit, poll interval, wind threshold)
```

- [ ] **Step 3: Commit**

```bash
git add .agent/docs/architecture.md .agent/docs/ui-design-system.md
git commit -m "docs: update architecture and UI design system with weather integration"
```

---

## Phase 9: End-to-End Integration Verification

### Task 33: Smoke Test the Full Stack

This is a manual verification task, not an automated test.

- [ ] **Step 1: Apply database migration**

```bash
cd src/FamilyHQ.WebApi
dotnet ef database update --project ../FamilyHQ.Data.PostgreSQL
```

- [ ] **Step 2: Start the simulator**

```bash
cd tools/FamilyHQ.Simulator
dotnet run
```

- [ ] **Step 3: Set test weather data via simulator backdoor**

```bash
curl -X POST https://localhost:7199/api/simulator/backdoor/weather \
  -H "Content-Type: application/json" \
  -d '{
    "latitude": 53.35,
    "longitude": -6.26,
    "current": { "weatherCode": 61, "temperature": 12.0, "windSpeed": 35.0 },
    "hourly": [
      { "time": "2026-04-01T09:00", "weatherCode": 0, "temperature": 10.0, "windSpeed": 15.0 },
      { "time": "2026-04-01T10:00", "weatherCode": 61, "temperature": 11.0, "windSpeed": 25.0 }
    ],
    "daily": [
      { "date": "2026-04-01", "weatherCode": 61, "temperatureMax": 14.0, "temperatureMin": 8.0, "windSpeedMax": 35.0 },
      { "date": "2026-04-02", "weatherCode": 0, "temperatureMax": 18.0, "temperatureMin": 9.0, "windSpeedMax": 12.0 }
    ]
  }'
```

- [ ] **Step 4: Start the WebApi and WebUi**

```bash
cd src/FamilyHQ.WebApi && dotnet run &
cd src/FamilyHQ.WebUi && dotnet run
```

- [ ] **Step 5: Verify in browser**

Open the app and confirm:
- Weather strip shows current conditions below the header
- Month view shows 5-day forecast in the strip
- Agenda view shows weather icons in date cells
- Day view shows hourly weather beside times
- Weather overlay animation is visible (rain with wind)
- Settings > Weather page loads and allows changes
- Toggling weather off hides all weather UI

- [ ] **Step 6: Build entire solution to verify no compilation errors**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 7: Run all unit tests**

Run: `dotnet test`
Expected: All tests pass
