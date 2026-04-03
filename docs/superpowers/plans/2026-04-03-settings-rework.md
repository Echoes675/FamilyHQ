# Settings Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the settings page into a tabbed layout (General / Location / Weather / Display), scope all settings per authenticated user in the DB, and add a manual theme selection with auto-change toggle.

**Architecture:** `Settings.razor` becomes a thin tab shell delegating to four Razor components (`SettingsGeneralTab`, `SettingsLocationTab`, `SettingsWeatherTab`, `SettingsDisplayTab`). All settings entities (`DisplaySetting`, `WeatherSetting`) gain a `UserId` column matched to the `LocationSetting` pattern. `ThemeSelection` (string `"auto"|"morning"|"daytime"|"evening"|"night"`) is added to `DisplaySetting` and persists the user's theme preference. `ThemeService` suppresses SignalR `ThemeChanged` events when the user has a manual selection active.

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core, PostgreSQL, Reqnroll/Playwright (E2E).

**Spec:** `docs/superpowers/specs/2026-04-03-settings-rework-design.md`

---

## File Map

### Create
- `src/FamilyHQ.WebUi/Components/Settings/SettingsGeneralTab.razor`
- `src/FamilyHQ.WebUi/Components/Settings/SettingsLocationTab.razor`
- `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherTab.razor`
- `src/FamilyHQ.WebUi/Components/Settings/SettingsDisplayTab.razor`
- Migrations (4): generated via `dotnet ef migrations add`

### Modify
- `src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs` — add `ThemeSelection`
- `src/FamilyHQ.Core/Models/DisplaySetting.cs` — add `UserId`, `ThemeSelection`
- `src/FamilyHQ.Core/Models/WeatherSetting.cs` — add `UserId`
- `src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs` — max multiplier 2.0→1.0, add ThemeSelection rule
- `src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs` — add `userId` param
- `src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs` — add `userId` param, add `GetAllAsync`
- `src/FamilyHQ.Core/Interfaces/IWeatherRefreshService.cs` — remove no-userId overload
- `src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs` — add UserId, ThemeSelection
- `src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs` — add UserId
- `src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs` — per-user
- `src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs` — per-user + `GetAllAsync`
- `src/FamilyHQ.Services/Weather/WeatherRefreshService.cs` — always per-user, no null path
- `src/FamilyHQ.Services/Weather/WeatherService.cs` — use `currentUserService.UserId!` for settings
- `src/FamilyHQ.Services/Weather/WeatherPollerService.cs` — iterate all users, shortest interval
- `src/FamilyHQ.WebApi/Controllers/SettingsController.cs` — remove `[AllowAnonymous]`, per-user display/weather
- `src/FamilyHQ.WebApi/Controllers/WeatherController.cs` — remove no-userId fallback in Refresh
- `src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs` — add `ApplyManualThemeAsync`, `IsAutoTheme`
- `src/FamilyHQ.WebUi/Services/DisplaySettingService.cs` — ThemeSelection support, theme application
- `src/FamilyHQ.WebUi/Services/IThemeService.cs` — add `ApplyCurrentPeriodAsync`
- `src/FamilyHQ.WebUi/Services/ThemeService.cs` — suppress SignalR when manual, check DisplaySettingService
- `src/FamilyHQ.WebUi/Layout/MainLayout.razor` — swap init order: DisplaySettingService first
- `src/FamilyHQ.WebUi/Pages/Settings.razor` — tabbed shell
- `tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs` — update for new ranges
- `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs` — per-user, ThemeSelection
- `tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs` — weather per-user
- `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs` — tab-based locators
- `tests-e2e/FamilyHQ.E2E.Common/Pages/WeatherSettingsPage.cs` — navigate via Weather tab
- `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs` — tab navigation steps
- `tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs` — weather tab navigation
- `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature` — tab-aware scenarios
- `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherSettings.feature` — tab-based navigation

### Delete
- `src/FamilyHQ.WebUi/Pages/WeatherSettings.razor`

---

## Task 1: Update DisplaySettingDto, validator, and validator tests

**Files:**
- Modify: `src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs`
- Modify: `src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs`
- Modify: `tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs`

- [ ] **Step 1: Update the failing validator tests first**

Open `tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs` and replace its content with:

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Validators;

public class DisplaySettingDtoValidatorTests
{
    private readonly DisplaySettingDtoValidator _sut = new();

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public async Task SurfaceMultiplier_Valid_PassesValidation(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public async Task SurfaceMultiplier_OutOfRange_FailsValidation(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SurfaceMultiplier");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task TransitionDuration_Valid_PassesValidation(int value)
    {
        var dto = new DisplaySettingDto(0.5, false, value, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task TransitionDuration_OutOfRange_FailsValidation(int value)
    {
        var dto = new DisplaySettingDto(0.5, false, value, "auto");
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransitionDurationSecs");
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("morning")]
    [InlineData("daytime")]
    [InlineData("evening")]
    [InlineData("night")]
    public async Task ThemeSelection_ValidValue_PassesValidation(string value)
    {
        var dto = new DisplaySettingDto(0.5, false, 15, value);
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("noon")]
    [InlineData("MORNING")]
    public async Task ThemeSelection_InvalidValue_FailsValidation(string value)
    {
        var dto = new DisplaySettingDto(0.5, false, 15, value);
        var result = await _sut.ValidateAsync(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ThemeSelection");
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

```bash
dotnet test tests/FamilyHQ.Core.Tests/FamilyHQ.Core.Tests.csproj --filter "DisplaySettingDtoValidatorTests" -v minimal
```

Expected: compile errors or test failures (DTO doesn't have ThemeSelection yet).

- [ ] **Step 3: Update DisplaySettingDto**

Replace `src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs`:

```csharp
namespace FamilyHQ.Core.DTOs;

public record DisplaySettingDto(
    double SurfaceMultiplier,
    bool OpaqueSurfaces,
    int TransitionDurationSecs,
    string ThemeSelection = "auto");
```

- [ ] **Step 4: Update DisplaySettingDtoValidator**

Replace `src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs`:

```csharp
using FamilyHQ.Core.DTOs;
using FluentValidation;

namespace FamilyHQ.Core.Validators;

public class DisplaySettingDtoValidator : AbstractValidator<DisplaySettingDto>
{
    private static readonly string[] ValidThemeSelections =
        ["auto", "morning", "daytime", "evening", "night"];

    public DisplaySettingDtoValidator()
    {
        RuleFor(x => x.SurfaceMultiplier)
            .InclusiveBetween(0.0, 1.0)
            .WithMessage("Surface multiplier must be between 0.0 and 1.0.");

        RuleFor(x => x.TransitionDurationSecs)
            .InclusiveBetween(0, 60)
            .WithMessage("Transition duration must be between 0 and 60 seconds.");

        RuleFor(x => x.ThemeSelection)
            .Must(v => ValidThemeSelections.Contains(v))
            .WithMessage("ThemeSelection must be one of: auto, morning, daytime, evening, night.");
    }
}
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test tests/FamilyHQ.Core.Tests/FamilyHQ.Core.Tests.csproj --filter "DisplaySettingDtoValidatorTests" -v minimal
```

Expected: all tests pass.

- [ ] **Step 6: Fix any compile errors from the DTO change**

The `DisplaySettingDto` record now has a new positional parameter with a default value. Run a build to find any broken call sites:

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj -v minimal
dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj -v minimal
```

Fix any `DisplaySettingDto(...)` constructor calls that don't include `ThemeSelection` — they will use the default `"auto"` automatically since it's a default parameter, so no changes should be needed. If there are test constructors with positional args, add `"auto"` as the 4th arg.

In `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`, update the DTO usages:
- `new DisplaySettingDto(1.2, false, 20)` → `new DisplaySettingDto(1.2, false, 20, "auto")`
- `new DisplaySettingDto(5.0, false, 100)` → `new DisplaySettingDto(5.0, false, 100, "auto")`
- The default assertion `dto.SurfaceMultiplier.Should().Be(1.0)` should still pass.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs \
        src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs \
        tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs \
        tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs
git commit -m "feat(settings): add ThemeSelection to DisplaySettingDto; cap SurfaceMultiplier at 1.0"
```

---

## Task 2: Add UserId and ThemeSelection to domain models; update EF configurations

**Files:**
- Modify: `src/FamilyHQ.Core/Models/DisplaySetting.cs`
- Modify: `src/FamilyHQ.Core/Models/WeatherSetting.cs`
- Modify: `src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs`
- Modify: `src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs`

- [ ] **Step 1: Update DisplaySetting model**

Replace `src/FamilyHQ.Core/Models/DisplaySetting.cs`:

```csharp
namespace FamilyHQ.Core.Models;

public class DisplaySetting
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public double SurfaceMultiplier { get; set; } = 1.0;
    public bool OpaqueSurfaces { get; set; }
    public int TransitionDurationSecs { get; set; } = 15;
    public string ThemeSelection { get; set; } = "auto";
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Update WeatherSetting model**

Replace `src/FamilyHQ.Core/Models/WeatherSetting.cs`:

```csharp
namespace FamilyHQ.Core.Models;

using FamilyHQ.Core.Enums;

public class WeatherSetting
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 30;
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public double WindThresholdKmh { get; set; } = 30;
    public string? ApiKey { get; set; }
}
```

- [ ] **Step 3: Update DisplaySettingConfiguration**

Replace `src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs`:

```csharp
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DisplaySettingConfiguration : IEntityTypeConfiguration<DisplaySetting>
{
    public void Configure(EntityTypeBuilder<DisplaySetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.SurfaceMultiplier).IsRequired();
        builder.Property(x => x.ThemeSelection).IsRequired().HasMaxLength(16).HasDefaultValue("auto");
        builder.Property(x => x.TransitionDurationSecs).IsRequired();
        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
```

- [ ] **Step 4: Update WeatherSettingConfiguration**

Replace `src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs`:

```csharp
namespace FamilyHQ.Data.Configurations;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class WeatherSettingConfiguration : IEntityTypeConfiguration<WeatherSetting>
{
    public void Configure(EntityTypeBuilder<WeatherSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.PollIntervalMinutes).IsRequired().HasDefaultValue(30);
        builder.Property(x => x.TemperatureUnit).IsRequired().HasDefaultValue(TemperatureUnit.Celsius);
        builder.Property(x => x.WindThresholdKmh).IsRequired().HasDefaultValue(30.0);
        builder.Property(x => x.ApiKey).HasMaxLength(500);
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
```

- [ ] **Step 5: Build to confirm no compile errors**

```bash
dotnet build src/FamilyHQ.Data/FamilyHQ.Data.csproj -v minimal
```

Expected: build succeeds (repository implementations may not compile yet — fix in Task 4).

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Core/Models/DisplaySetting.cs \
        src/FamilyHQ.Core/Models/WeatherSetting.cs \
        src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs \
        src/FamilyHQ.Data/Configurations/WeatherSettingConfiguration.cs
git commit -m "feat(settings): add UserId and ThemeSelection to DisplaySetting and WeatherSetting models"
```

---

## Task 3: Update repository interfaces

**Files:**
- Modify: `src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/IWeatherRefreshService.cs`

- [ ] **Step 1: Update IDisplaySettingRepository**

Replace `src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs`:

```csharp
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDisplaySettingRepository
{
    Task<DisplaySetting?> GetAsync(string userId, CancellationToken ct = default);
    Task<DisplaySetting> UpsertAsync(string userId, DisplaySetting setting, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update IWeatherSettingRepository**

Replace `src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs`:

```csharp
namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWeatherSettingRepository
{
    Task<WeatherSetting> GetOrCreateAsync(string userId, CancellationToken ct = default);
    Task<WeatherSetting> UpsertAsync(string userId, WeatherSetting setting, CancellationToken ct = default);
    Task<List<WeatherSetting>> GetAllAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Update IWeatherRefreshService — remove no-userId overload**

Replace `src/FamilyHQ.Core/Interfaces/IWeatherRefreshService.cs`:

```csharp
namespace FamilyHQ.Core.Interfaces;

public interface IWeatherRefreshService
{
    /// <summary>Refreshes weather using the specified user's saved location and settings.</summary>
    Task RefreshAsync(string userId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build Core to surface all broken callers**

```bash
dotnet build FamilyHQ.sln -v minimal 2>&1 | grep -i "error"
```

Expected: compile errors in `DisplaySettingRepository`, `WeatherSettingRepository`, `WeatherRefreshService`, `WeatherPollerService`, `WeatherController`, and `SettingsController`. These are fixed in subsequent tasks.

- [ ] **Step 5: Commit interfaces**

```bash
git add src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs \
        src/FamilyHQ.Core/Interfaces/IWeatherSettingRepository.cs \
        src/FamilyHQ.Core/Interfaces/IWeatherRefreshService.cs
git commit -m "feat(settings): update repository interfaces for per-user scoping"
```

---

## Task 4: Update repository implementations and unit tests

**Files:**
- Modify: `src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs`
- Modify: `src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs`
- Modify: `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`

- [ ] **Step 1: Update DisplaySettingRepository**

Replace `src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs`:

```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DisplaySettingRepository : IDisplaySettingRepository
{
    private readonly FamilyHqDbContext _context;

    public DisplaySettingRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<DisplaySetting?> GetAsync(string userId, CancellationToken ct = default)
        => await _context.DisplaySettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<DisplaySetting> UpsertAsync(string userId, DisplaySetting setting, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is null)
        {
            setting.UserId = userId;
            _context.DisplaySettings.Add(setting);
            await _context.SaveChangesAsync(ct);
            return setting;
        }

        existing.SurfaceMultiplier = setting.SurfaceMultiplier;
        existing.OpaqueSurfaces = setting.OpaqueSurfaces;
        existing.TransitionDurationSecs = setting.TransitionDurationSecs;
        existing.ThemeSelection = setting.ThemeSelection;
        existing.UpdatedAt = setting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
```

- [ ] **Step 2: Update WeatherSettingRepository**

Replace `src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs`:

```csharp
namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

public class WeatherSettingRepository(FamilyHqDbContext context) : IWeatherSettingRepository
{
    public async Task<WeatherSetting> GetOrCreateAsync(string userId, CancellationToken ct = default)
    {
        var setting = await context.WeatherSettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (setting is not null)
            return setting;

        setting = new WeatherSetting { UserId = userId };
        context.WeatherSettings.Add(setting);
        await context.SaveChangesAsync(ct);
        return setting;
    }

    public async Task<WeatherSetting> UpsertAsync(string userId, WeatherSetting setting, CancellationToken ct = default)
    {
        var existing = await context.WeatherSettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is null)
        {
            setting.UserId = userId;
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

    public async Task<List<WeatherSetting>> GetAllAsync(CancellationToken ct = default)
        => await context.WeatherSettings.AsNoTracking().ToListAsync(ct);
}
```

- [ ] **Step 3: Update DisplaySettingsControllerTests mock setup**

In `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`, update the two mock setups from the old no-userId signature to the new one:

Change:
```csharp
displayRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
```
To:
```csharp
displayRepoMock.Setup(x => x.GetAsync("test-user-123", It.IsAny<CancellationToken>()))
```

And change:
```csharp
displayRepoMock.Setup(x => x.UpsertAsync(It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((DisplaySetting ds, CancellationToken _) => ds);
```
To:
```csharp
displayRepoMock.Setup(x => x.UpsertAsync("test-user-123", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((string _, DisplaySetting ds, CancellationToken _) => ds);
```

Also update the `Verify` call:
```csharp
displayRepoMock.Verify(x => x.UpsertAsync(
    "test-user-123",
    It.Is<DisplaySetting>(ds =>
        ds.SurfaceMultiplier == 1.2 &&
        ds.TransitionDurationSecs == 20 &&
        !ds.OpaqueSurfaces),
    It.IsAny<CancellationToken>()), Times.Once);
```

- [ ] **Step 4: Build the data layer**

```bash
dotnet build src/FamilyHQ.Data/FamilyHQ.Data.csproj -v minimal
```

Expected: builds cleanly.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs \
        src/FamilyHQ.Data/Repositories/WeatherSettingRepository.cs \
        tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs
git commit -m "feat(settings): update DisplaySettingRepository and WeatherSettingRepository for per-user scoping"
```

---

## Task 5: Apply DB migrations

**Files:**
- Create: migrations in `src/FamilyHQ.Data.PostgreSQL/Migrations/`

> Run all migration commands from the repo root. The WebApi project is the startup project; the PostgreSQL project holds the migrations.

- [ ] **Step 1: Add migration — AddUserIdToDisplaySetting**

```bash
dotnet ef migrations add AddUserIdToDisplaySetting \
  --project src/FamilyHQ.Data.PostgreSQL \
  --startup-project src/FamilyHQ.WebApi \
  --output-dir Migrations
```

Open the generated `Up` method and add a data step to delete any orphaned rows (rows without a user), then make the column non-nullable. The generated migration will add the column as nullable (EF default for a new required string). Edit the `Up` method to:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Add as nullable first
    migrationBuilder.AddColumn<string>(
        name: "UserId",
        table: "DisplaySettings",
        type: "character varying(256)",
        maxLength: 256,
        nullable: true);

    // Remove orphaned rows that have no owner
    migrationBuilder.Sql("DELETE FROM \"DisplaySettings\" WHERE \"UserId\" IS NULL;");

    // Make non-nullable
    migrationBuilder.AlterColumn<string>(
        name: "UserId",
        table: "DisplaySettings",
        type: "character varying(256)",
        maxLength: 256,
        nullable: false,
        oldClrType: typeof(string),
        oldType: "character varying(256)",
        oldMaxLength: 256,
        oldNullable: true);

    migrationBuilder.CreateIndex(
        name: "IX_DisplaySettings_UserId",
        table: "DisplaySettings",
        column: "UserId",
        unique: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_DisplaySettings_UserId",
        table: "DisplaySettings");

    migrationBuilder.DropColumn(
        name: "UserId",
        table: "DisplaySettings");
}
```

- [ ] **Step 2: Add migration — AddUserIdToWeatherSetting**

```bash
dotnet ef migrations add AddUserIdToWeatherSetting \
  --project src/FamilyHQ.Data.PostgreSQL \
  --startup-project src/FamilyHQ.WebApi \
  --output-dir Migrations
```

Edit the generated migration `Up` method with the same pattern (add nullable, delete orphans, make non-nullable, add unique index):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "UserId",
        table: "WeatherSettings",
        type: "character varying(256)",
        maxLength: 256,
        nullable: true);

    migrationBuilder.Sql("DELETE FROM \"WeatherSettings\" WHERE \"UserId\" IS NULL;");

    migrationBuilder.AlterColumn<string>(
        name: "UserId",
        table: "WeatherSettings",
        type: "character varying(256)",
        maxLength: 256,
        nullable: false,
        oldClrType: typeof(string),
        oldType: "character varying(256)",
        oldMaxLength: 256,
        oldNullable: true);

    migrationBuilder.CreateIndex(
        name: "IX_WeatherSettings_UserId",
        table: "WeatherSettings",
        column: "UserId",
        unique: true);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "IX_WeatherSettings_UserId",
        table: "WeatherSettings");

    migrationBuilder.DropColumn(
        name: "UserId",
        table: "WeatherSettings");
}
```

- [ ] **Step 3: Add migration — AddThemeSelectionToDisplaySetting**

```bash
dotnet ef migrations add AddThemeSelectionToDisplaySetting \
  --project src/FamilyHQ.Data.PostgreSQL \
  --startup-project src/FamilyHQ.WebApi \
  --output-dir Migrations
```

EF will generate `AddColumn<string>("ThemeSelection", ...)`. Verify the generated migration adds the column with default `"auto"` and max length 16. It should look like:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "ThemeSelection",
        table: "DisplaySettings",
        type: "character varying(16)",
        maxLength: 16,
        nullable: false,
        defaultValue: "auto");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropColumn(
        name: "ThemeSelection",
        table: "DisplaySettings");
}
```

If EF generates a different default value (empty string), edit it to `defaultValue: "auto"`.

- [ ] **Step 4: Add migration — RescaleSurfaceMultiplier**

```bash
dotnet ef migrations add RescaleSurfaceMultiplier \
  --project src/FamilyHQ.Data.PostgreSQL \
  --startup-project src/FamilyHQ.WebApi \
  --output-dir Migrations
```

This migration has no schema change — only a data update. EF will generate an empty migration. Edit it to:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Rescale existing SurfaceMultiplier values from 0–2.0 to 0–1.0
    migrationBuilder.Sql(
        "UPDATE \"DisplaySettings\" SET \"SurfaceMultiplier\" = LEAST(\"SurfaceMultiplier\" / 2.0, 1.0);");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // Undo rescaling (approximate — multiply back by 2)
    migrationBuilder.Sql(
        "UPDATE \"DisplaySettings\" SET \"SurfaceMultiplier\" = \"SurfaceMultiplier\" * 2.0;");
}
```

- [ ] **Step 5: Apply all migrations**

Ensure the WebApi is configured to connect to the dev database, then:

```bash
dotnet ef database update \
  --project src/FamilyHQ.Data.PostgreSQL \
  --startup-project src/FamilyHQ.WebApi
```

Expected: all 4 new migrations applied successfully.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git commit -m "feat(settings): add per-user scoping and ThemeSelection migrations; rescale SurfaceMultiplier to 0–1.0"
```

---

## Task 6: Update WeatherRefreshService and WeatherService

**Files:**
- Modify: `src/FamilyHQ.Services/Weather/WeatherRefreshService.cs`
- Modify: `src/FamilyHQ.Services/Weather/WeatherService.cs`

- [ ] **Step 1: Update WeatherRefreshService — always per-user**

Replace `src/FamilyHQ.Services/Weather/WeatherRefreshService.cs`:

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

public class WeatherRefreshService(
    IWeatherSettingRepository weatherSettingRepo,
    ILocationSettingRepository locationRepo,
    IWeatherProvider weatherProvider,
    IWeatherDataPointRepository weatherDataPointRepo,
    IWeatherBroadcaster weatherBroadcaster,
    ILogger<WeatherRefreshService> logger) : IWeatherRefreshService
{
    public async Task RefreshAsync(string userId, CancellationToken ct = default)
    {
        var weatherSetting = await weatherSettingRepo.GetOrCreateAsync(userId, ct);

        if (!weatherSetting.Enabled)
        {
            logger.LogInformation("Weather is disabled for user {UserId}. Skipping refresh.", userId);
            return;
        }

        var location = await locationRepo.GetAsync(userId, ct);

        if (location is null)
        {
            logger.LogInformation("No location configured for user {UserId}. Skipping weather refresh.", userId);
            return;
        }

        var weatherResponse = await weatherProvider.GetWeatherAsync(
            location.Latitude, location.Longitude, ct);

        var now = DateTimeOffset.UtcNow;
        var windThreshold = weatherSetting.WindThresholdKmh;

        var dataPoints = BuildDataPoints(location.Id, weatherResponse, now, windThreshold);

        await weatherDataPointRepo.ReplaceAllAsync(location.Id, dataPoints, ct);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(ct);

        logger.LogInformation(
            "Weather data updated for user {UserId}, location {PlaceName} ({Lat}, {Lon})",
            userId, location.PlaceName, location.Latitude, location.Longitude);
    }

    internal static List<WeatherDataPoint> BuildDataPoints(
        int locationSettingId,
        WeatherResponse response,
        DateTimeOffset retrievedAt,
        double windThresholdKmh)
    {
        var dataPoints = new List<WeatherDataPoint>();

        dataPoints.Add(new WeatherDataPoint
        {
            LocationSettingId = locationSettingId,
            Timestamp = retrievedAt,
            Condition = response.CurrentCondition,
            TemperatureCelsius = response.CurrentTemperatureCelsius,
            WindSpeedKmh = response.CurrentWindSpeedKmh,
            IsWindy = response.CurrentWindSpeedKmh >= windThresholdKmh,
            DataType = WeatherDataType.Current,
            RetrievedAt = retrievedAt
        });

        foreach (var hourly in response.HourlyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = hourly.Time,
                Condition = hourly.Condition,
                TemperatureCelsius = hourly.TemperatureCelsius,
                WindSpeedKmh = hourly.WindSpeedKmh,
                IsWindy = hourly.WindSpeedKmh >= windThresholdKmh,
                DataType = WeatherDataType.Hourly,
                RetrievedAt = retrievedAt
            });
        }

        foreach (var daily in response.DailyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = new DateTimeOffset(daily.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Condition = daily.Condition,
                TemperatureCelsius = daily.HighCelsius,
                HighCelsius = daily.HighCelsius,
                LowCelsius = daily.LowCelsius,
                WindSpeedKmh = daily.WindSpeedMaxKmh,
                IsWindy = daily.WindSpeedMaxKmh >= windThresholdKmh,
                DataType = WeatherDataType.Daily,
                RetrievedAt = retrievedAt
            });
        }

        return dataPoints;
    }
}
```

- [ ] **Step 2: Update WeatherService — per-user settings**

In `src/FamilyHQ.Services/Weather/WeatherService.cs`, find every call to `weatherSettingRepository.GetOrCreateAsync(ct)` (no userId) and replace with `weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct)`.

There are calls in: `GetCurrentAsync`, `GetHourlyAsync`, `GetDailyForecastAsync`, `GetSettingsAsync`, `UpdateSettingsAsync`. Each one should use `currentUserService.UserId!`.

The complete updated WeatherService should look like (showing the relevant method signatures only — keep all other implementation details):

```csharp
public async Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default)
{
    var locationId = await GetLocationSettingIdAsync(ct);
    if (locationId is null) return null;

    var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
    var dataPoint = await weatherDataPointRepository.GetCurrentAsync(locationId.Value, ct);
    if (dataPoint is null) return null;

    return MapToCurrentDto(dataPoint, setting.TemperatureUnit);
}

public async Task<WeatherSettingDto> GetSettingsAsync(CancellationToken ct = default)
{
    var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
    return MapToDto(setting, maskApiKey: true);
}

public async Task<WeatherSettingDto> UpdateSettingsAsync(WeatherSettingDto dto, CancellationToken ct = default)
{
    var userId = currentUserService.UserId!;
    var existing = await weatherSettingRepository.GetOrCreateAsync(userId, ct);
    existing.Enabled = dto.Enabled;
    existing.PollIntervalMinutes = dto.PollIntervalMinutes;
    existing.TemperatureUnit = dto.TemperatureUnit;
    existing.WindThresholdKmh = dto.WindThresholdKmh;
    var updated = await weatherSettingRepository.UpsertAsync(userId, existing, ct);
    return MapToDto(updated, maskApiKey: true);
}
```

Apply the same pattern to `GetHourlyAsync` and `GetDailyForecastAsync`.

- [ ] **Step 3: Build the Services project**

```bash
dotnet build src/FamilyHQ.Services/FamilyHQ.Services.csproj -v minimal
```

Expected: builds cleanly.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.Services/Weather/WeatherRefreshService.cs \
        src/FamilyHQ.Services/Weather/WeatherService.cs
git commit -m "feat(settings): update WeatherRefreshService and WeatherService for per-user scoping"
```

---

## Task 7: Update WeatherPollerService for multi-user polling

**Files:**
- Modify: `src/FamilyHQ.Services/Weather/WeatherPollerService.cs`

- [ ] **Step 1: Write the updated WeatherPollerService**

Replace `src/FamilyHQ.Services/Weather/WeatherPollerService.cs`:

```csharp
namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class WeatherPollerService(
    IServiceProvider serviceProvider,
    ILogger<WeatherPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPollIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weather poll iteration failed. Retrying in {Delay}.", RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task RunPollIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var refreshService = scope.ServiceProvider.GetRequiredService<IWeatherRefreshService>();
        var weatherSettingRepo = scope.ServiceProvider.GetRequiredService<IWeatherSettingRepository>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<WeatherOptions>>().Value;

        var allSettings = await weatherSettingRepo.GetAllAsync(stoppingToken);
        var enabledSettings = allSettings.Where(s => s.Enabled).ToList();

        // Refresh weather for each user with weather enabled
        foreach (var setting in enabledSettings)
        {
            try
            {
                await refreshService.RefreshAsync(setting.UserId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weather refresh failed for user {UserId}.", setting.UserId);
            }
        }

        // Poll at the shortest interval across all enabled users, clamped to min interval
        var shortestInterval = enabledSettings.Count > 0
            ? enabledSettings.Min(s => s.PollIntervalMinutes)
            : (int)DefaultPollInterval.TotalMinutes;

        var pollInterval = TimeSpan.FromMinutes(
            Math.Max(options.MinPollIntervalMinutes, shortestInterval));

        await Task.Delay(pollInterval, stoppingToken);
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build src/FamilyHQ.Services/FamilyHQ.Services.csproj -v minimal
```

Expected: builds cleanly.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.Services/Weather/WeatherPollerService.cs
git commit -m "feat(settings): update WeatherPollerService to iterate all users with shortest enabled interval"
```

---

## Task 8: Update SettingsController and WeatherController; update controller unit tests

**Files:**
- Modify: `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`
- Modify: `src/FamilyHQ.WebApi/Controllers/WeatherController.cs`
- Modify: `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`
- Modify: `tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs`

- [ ] **Step 1: Update SettingsController**

In `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`, make the following changes:

**GetDisplay** — remove `[AllowAnonymous]`, scope to userId:
```csharp
[HttpGet("display")]
public async Task<IActionResult> GetDisplay(CancellationToken ct)
{
    var userId = _currentUser.UserId!;
    var setting = await _displayRepo.GetAsync(userId, ct);
    if (setting is null)
        return Ok(new DisplaySettingDto(1.0, false, 15, "auto"));

    return Ok(new DisplaySettingDto(
        setting.SurfaceMultiplier,
        setting.OpaqueSurfaces,
        setting.TransitionDurationSecs,
        setting.ThemeSelection));
}
```

**PutDisplay** — remove `[AllowAnonymous]`, scope to userId, include ThemeSelection:
```csharp
[HttpPut("display")]
public async Task<IActionResult> PutDisplay([FromBody] DisplaySettingDto dto, CancellationToken ct)
{
    var validator = new DisplaySettingDtoValidator();
    var validation = await validator.ValidateAsync(dto, ct);
    if (!validation.IsValid)
        return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

    var userId = _currentUser.UserId!;
    var setting = new DisplaySetting
    {
        SurfaceMultiplier = dto.SurfaceMultiplier,
        OpaqueSurfaces = dto.OpaqueSurfaces,
        TransitionDurationSecs = dto.TransitionDurationSecs,
        ThemeSelection = dto.ThemeSelection,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await _displayRepo.UpsertAsync(userId, setting, ct);
    return Ok(dto);
}
```

**GetWeatherSettings** — remove `[AllowAnonymous]`:
```csharp
[HttpGet("weather")]
public async Task<IActionResult> GetWeatherSettings(CancellationToken ct)
{
    var dto = await _weatherService.GetSettingsAsync(ct);
    return Ok(dto);
}
```

**UpdateWeatherSettings** — remove `[AllowAnonymous]`:
```csharp
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

- [ ] **Step 2: Update WeatherController.Refresh — remove no-userId fallback**

In `src/FamilyHQ.WebApi/Controllers/WeatherController.cs`, update the `Refresh` action:

```csharp
[AllowAnonymous]
[HttpPost("refresh")]
public async Task<IActionResult> Refresh(CancellationToken ct)
{
    var userId = currentUser.UserId;
    if (userId is null)
        return Unauthorized();
    await weatherRefreshService.RefreshAsync(userId, ct);
    return Ok(new { message = "Weather refreshed" });
}
```

- [ ] **Step 3: Update DisplaySettingsControllerTests — add ThemeSelection assertions**

In `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`:

Update `GetDisplay_ReturnsDefaults_WhenNotSet` to assert on the default ThemeSelection:
```csharp
dto.ThemeSelection.Should().Be("auto");
```

Update `GetDisplay_ReturnsSaved_WhenSet` — update the mock return to include ThemeSelection and verify:
```csharp
displayRepoMock.Setup(x => x.GetAsync("test-user-123", It.IsAny<CancellationToken>()))
    .ReturnsAsync(new DisplaySetting
    {
        UserId = "test-user-123",
        SurfaceMultiplier = 0.5,
        OpaqueSurfaces = true,
        TransitionDurationSecs = 30,
        ThemeSelection = "evening"
    });
// ...
dto.ThemeSelection.Should().Be("evening");
```

Update `PutDisplay_ValidDto_UpsertAndReturnsOk` — use values within the new 0–1.0 range:
```csharp
var dto = new DisplaySettingDto(0.8, false, 20, "morning");
// ...
displayRepoMock.Verify(x => x.UpsertAsync(
    "test-user-123",
    It.Is<DisplaySetting>(ds =>
        ds.SurfaceMultiplier == 0.8 &&
        ds.TransitionDurationSecs == 20 &&
        !ds.OpaqueSurfaces &&
        ds.ThemeSelection == "morning"),
    It.IsAny<CancellationToken>()), Times.Once);
```

Update `PutDisplay_InvalidDto_ReturnsBadRequest` — use a value above the new max:
```csharp
var dto = new DisplaySettingDto(1.5, false, 100, "auto"); // 1.5 > 1.0, duration 100 > 60
```

- [ ] **Step 4: Run controller tests**

```bash
dotnet test tests/FamilyHQ.WebApi.Tests/FamilyHQ.WebApi.Tests.csproj -v minimal
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/SettingsController.cs \
        src/FamilyHQ.WebApi/Controllers/WeatherController.cs \
        tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs \
        tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs
git commit -m "feat(settings): scope display and weather API endpoints to authenticated user; add ThemeSelection"
```

---

## Task 9: Update DisplaySettingService and ThemeService; swap MainLayout init order

**Files:**
- Modify: `src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/DisplaySettingService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/IThemeService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/ThemeService.cs`
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`

- [ ] **Step 1: Update IDisplaySettingService**

Replace `src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs`:

```csharp
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface IDisplaySettingService : IAsyncDisposable
{
    Task InitialiseAsync();
    Task UpdatePropertyAsync(string cssPropertyName, string value);
    Task SaveAsync(DisplaySettingDto dto);
    Task ApplyManualThemeAsync(string themeName);
    DisplaySettingDto CurrentSettings { get; }
    bool IsAutoTheme { get; }
}
```

- [ ] **Step 2: Update DisplaySettingService**

Replace `src/FamilyHQ.WebUi/Services/DisplaySettingService.cs`:

```csharp
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class DisplaySettingService : IDisplaySettingService, IAsyncDisposable
{
    private readonly ISettingsApiService _settingsApi;
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;

    public DisplaySettingDto CurrentSettings { get; private set; } =
        new(1.0, false, 15, "auto");

    public bool IsAutoTheme => CurrentSettings.ThemeSelection == "auto";

    public DisplaySettingService(ISettingsApiService settingsApi, IJSRuntime jsRuntime)
    {
        _settingsApi = settingsApi;
        _jsRuntime = jsRuntime;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            CurrentSettings = await _settingsApi.GetDisplayAsync();
        }
        catch
        {
            // Use defaults if API call fails
        }

        await ApplyAllPropertiesAsync();
    }

    public async Task UpdatePropertyAsync(string cssPropertyName, string value)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("setDisplayProperty", cssPropertyName, value);
    }

    public async Task SaveAsync(DisplaySettingDto dto)
    {
        CurrentSettings = await _settingsApi.SaveDisplayAsync(dto);
        await ApplyAllPropertiesAsync();
    }

    public async Task ApplyManualThemeAsync(string themeName)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("setTheme", themeName);
    }

    private async Task ApplyAllPropertiesAsync()
    {
        var module = await GetModuleAsync();

        var multiplier = CurrentSettings.OpaqueSurfaces ? "1.0" : CurrentSettings.SurfaceMultiplier.ToString("F2");
        await module.InvokeVoidAsync("setDisplayProperty", "--user-surface-multiplier", multiplier);

        var duration = $"{CurrentSettings.TransitionDurationSecs}s";
        await module.InvokeVoidAsync("setDisplayProperty", "--theme-transition-duration", duration);

        // Apply manual theme if set
        if (!IsAutoTheme)
            await module.InvokeVoidAsync("setTheme", CurrentSettings.ThemeSelection);
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        return _module;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
```

- [ ] **Step 3: Update IThemeService**

Replace `src/FamilyHQ.WebUi/Services/IThemeService.cs`:

```csharp
namespace FamilyHQ.WebUi.Services;

public interface IThemeService : IAsyncDisposable
{
    Task InitialiseAsync();
    Task ApplyCurrentPeriodAsync();
}
```

- [ ] **Step 4: Update ThemeService — suppress SignalR when manual theme is active**

Replace `src/FamilyHQ.WebUi/Services/ThemeService.cs`:

```csharp
using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class ThemeService : IThemeService, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly SignalRService _signalRService;
    private readonly IDisplaySettingService _displaySettingService;
    private readonly Action<string> _themeChangedHandler;
    private IJSObjectReference? _module;

    public ThemeService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        SignalRService signalRService,
        IDisplaySettingService displaySettingService)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _signalRService = signalRService;
        _displaySettingService = displaySettingService;

        _themeChangedHandler = period =>
        {
            if (_displaySettingService.IsAutoTheme)
                _ = SetThemeAsync(period);
        };
        _signalRService.OnThemeChanged += _themeChangedHandler;
    }

    public async Task InitialiseAsync()
    {
        // DisplaySettingService.InitialiseAsync() runs before ThemeService.InitialiseAsync()
        // so CurrentSettings.ThemeSelection is already loaded.
        if (!_displaySettingService.IsAutoTheme)
        {
            await SetThemeAsync(_displaySettingService.CurrentSettings.ThemeSelection);
            return;
        }

        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
            if (dto is not null)
                await SetThemeAsync(dto.CurrentPeriod);
        }
        catch (HttpRequestException)
        {
            // Theme is non-critical
        }
    }

    public async Task ApplyCurrentPeriodAsync()
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today");
            if (dto is not null)
                await SetThemeAsync(dto.CurrentPeriod);
        }
        catch (HttpRequestException) { }
    }

    private async Task SetThemeAsync(string period)
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        await _module.InvokeVoidAsync("setTheme", period);
    }

    public async ValueTask DisposeAsync()
    {
        _signalRService.OnThemeChanged -= _themeChangedHandler;
        if (_module is not null)
            await _module.DisposeAsync();
    }
}
```

- [ ] **Step 5: Swap init order in MainLayout.razor**

In `src/FamilyHQ.WebUi/Layout/MainLayout.razor`, change:
```csharp
protected override async Task OnInitializedAsync()
{
    await ThemeService.InitialiseAsync();
    await DisplaySettingService.InitialiseAsync();
    await WeatherUiService.InitialiseAsync();
}
```
To:
```csharp
protected override async Task OnInitializedAsync()
{
    await DisplaySettingService.InitialiseAsync();  // must run first so ThemeService can read ThemeSelection
    await ThemeService.InitialiseAsync();
    await WeatherUiService.InitialiseAsync();
}
```

- [ ] **Step 6: Build the WebUi project**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj -v minimal
```

Expected: builds cleanly. If the DI container throws because `ThemeService` now needs `IDisplaySettingService`, check the DI registration in `Program.cs` — both are already registered as scoped, so injection should work automatically.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs \
        src/FamilyHQ.WebUi/Services/DisplaySettingService.cs \
        src/FamilyHQ.WebUi/Services/IThemeService.cs \
        src/FamilyHQ.WebUi/Services/ThemeService.cs \
        src/FamilyHQ.WebUi/Layout/MainLayout.razor
git commit -m "feat(settings): add ThemeSelection support to DisplaySettingService; ThemeService respects manual theme"
```

---

## Task 10: Rework Settings.razor to tabbed shell and create 4 tab components

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor`
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsGeneralTab.razor`
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsLocationTab.razor`
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherTab.razor`
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsDisplayTab.razor`

- [ ] **Step 1: Rework Settings.razor as a thin tabbed shell**

Replace `src/FamilyHQ.WebUi/Pages/Settings.razor` with:

```razor
@page "/settings"
@using FamilyHQ.WebUi.Services.Auth
@using FamilyHQ.WebUi.Components.Settings
@inject IAuthenticationService AuthService
@inject NavigationManager Navigation

<DashboardHeader BackUrl="/" />

<div class="settings-page settings-page--tabbed">
    <nav class="settings-tab-strip" aria-label="Settings sections">
        <button class="settings-tab @(_activeTab == "general"  ? "settings-tab--active" : "")"
                data-testid="tab-general"
                @onclick='() => SetTab("general")'>
            <span class="settings-tab__icon" aria-hidden="true">⚙</span>
            <span class="settings-tab__label">General</span>
        </button>
        <button class="settings-tab @(_activeTab == "location" ? "settings-tab--active" : "")"
                data-testid="tab-location"
                @onclick='() => SetTab("location")'>
            <span class="settings-tab__icon" aria-hidden="true">📍</span>
            <span class="settings-tab__label">Location</span>
        </button>
        <button class="settings-tab @(_activeTab == "weather"  ? "settings-tab--active" : "")"
                data-testid="tab-weather"
                @onclick='() => SetTab("weather")'>
            <span class="settings-tab__icon" aria-hidden="true">🌤</span>
            <span class="settings-tab__label">Weather</span>
        </button>
        <button class="settings-tab @(_activeTab == "display"  ? "settings-tab--active" : "")"
                data-testid="tab-display"
                @onclick='() => SetTab("display")'>
            <span class="settings-tab__icon" aria-hidden="true">🖥</span>
            <span class="settings-tab__label">Display</span>
        </button>
    </nav>

    <div class="settings-tab-content">
        @switch (_activeTab)
        {
            case "general":
                <SettingsGeneralTab />
                break;
            case "location":
                <SettingsLocationTab />
                break;
            case "weather":
                <SettingsWeatherTab />
                break;
            case "display":
                <SettingsDisplayTab />
                break;
        }
    </div>
</div>

@code {
    private string _activeTab = "general";

    protected override async Task OnInitializedAsync()
    {
        if (!await AuthService.IsAuthenticatedAsync())
            Navigation.NavigateTo("/");
    }

    private void SetTab(string tab) => _activeTab = tab;
}
```

- [ ] **Step 2: Create SettingsGeneralTab.razor**

Create `src/FamilyHQ.WebUi/Components/Settings/SettingsGeneralTab.razor`:

```razor
@using FamilyHQ.WebUi.Services.Auth
@inject IAuthenticationService AuthService
@inject NavigationManager Navigation

<div class="settings-section-content">
    <h2 class="settings-section__title">General</h2>

    @if (_username is not null)
    {
        <div class="settings-account glass-surface">
            <div class="account-avatar">@GetInitials(_username)</div>
            <p class="account-name" data-testid="account-name">@_username</p>
        </div>
    }

    <button class="settings-btn settings-btn--secondary"
            data-testid="sign-out-btn"
            @onclick="OnSignOutClicked">
        Sign Out
    </button>
</div>

@code {
    private string? _username;

    protected override async Task OnInitializedAsync()
    {
        _username = await AuthService.GetUsernameAsync();
    }

    private async Task OnSignOutClicked()
    {
        await AuthService.SignOutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
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

- [ ] **Step 3: Create SettingsLocationTab.razor**

Create `src/FamilyHQ.WebUi/Components/Settings/SettingsLocationTab.razor`:

```razor
@using FamilyHQ.Core.DTOs
@using FamilyHQ.WebUi.Services
@inject ISettingsApiService SettingsApiService
@inject IJSRuntime JS

<div class="settings-section-content">
    <h2 class="settings-section__title">Location</h2>

    <div class="settings-section">
        <p class="settings-label">Current location</p>
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
            <label for="place-input" class="settings-label">Override location</label>
            <input id="place-input"
                   type="text"
                   class="settings-input @(!string.IsNullOrWhiteSpace(_saveError) ? "settings-input--error" : "")"
                   @bind="_placeNameInput"
                   @bind:event="oninput"
                   @onfocus="OnInputFocus"
                   placeholder="e.g. Edinburgh, Scotland" />
            @if (!string.IsNullOrWhiteSpace(_saveError))
            {
                <p class="settings-field-error">@_saveError</p>
            }
        </div>

        <button class="settings-btn" data-testid="save-location-btn" @onclick="SaveLocationAsync" disabled="@_isSaving">
            @(_isSaving ? "Saving…" : (_locationSetting is { IsAutoDetected: false } ? "Update Location" : "Save Location"))
        </button>
        @if (_locationSetting is { IsAutoDetected: false })
        {
            <button class="settings-btn settings-btn--ghost" @onclick="ResetToAutoLocationAsync" disabled="@_isSaving">
                Reset to auto-detect
            </button>
        }
        <p class="settings-hint">Takes effect immediately.</p>
    </div>
</div>

@code {
    private LocationSettingDto? _locationSetting;
    private string _placeNameInput = string.Empty;
    private bool _isSaving;
    private string? _saveError;
    private ElementReference _scrollContainer;

    protected override async Task OnInitializedAsync()
    {
        _locationSetting = await SettingsApiService.GetLocationAsync();
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
        }
        catch (InvalidOperationException)
        {
            _saveError = "Location not found. Please check the spelling and try again.";
        }
        catch (Exception)
        {
            _saveError = "Could not save location. Please try again.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task ResetToAutoLocationAsync()
    {
        _isSaving = true;
        _saveError = null;
        try
        {
            await SettingsApiService.DeleteLocationAsync();
            _locationSetting = null;
            _placeNameInput = string.Empty;
        }
        catch (Exception)
        {
            _saveError = "Could not reset location. Please try again.";
        }
        finally
        {
            _isSaving = false;
        }
    }
}
```

- [ ] **Step 4: Create SettingsWeatherTab.razor**

Create `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherTab.razor`:

```razor
@using FamilyHQ.Core.DTOs
@using FamilyHQ.Core.Enums
@using FamilyHQ.WebUi.Services
@inject IWeatherUiService WeatherService

<div class="settings-section-content">
    <h2 class="settings-section__title">Weather</h2>
    <div class="settings-section">
        @if (_isLoading)
        {
            <p class="settings-hint">Loading…</p>
        }
        else
        {
            <div class="form-check mb-4">
                <input type="checkbox" class="form-check-input" id="weather-enabled-toggle"
                       checked="@_enabled" @onchange="OnEnabledToggled" />
                <label class="form-check-label" for="weather-enabled-toggle">Enable weather display</label>
            </div>

            <div class="mb-4">
                <label class="settings-label" for="temperature-unit">Temperature Unit</label>
                <select id="temperature-unit"
                        class="settings-input"
                        value="@_temperatureUnit"
                        @onchange="OnTemperatureUnitChanged">
                    <option value="@TemperatureUnit.Celsius">Celsius (°C)</option>
                    <option value="@TemperatureUnit.Fahrenheit">Fahrenheit (°F)</option>
                </select>
            </div>

            <div class="settings-input-container mb-4">
                <label class="settings-label" for="poll-interval">Poll Interval (minutes)</label>
                <input id="poll-interval"
                       type="number"
                       class="settings-input"
                       min="1"
                       value="@_pollIntervalMinutes"
                       @onchange="OnPollIntervalChanged" />
                <p class="settings-hint">How often weather data is refreshed.</p>
            </div>

            <div class="settings-input-container mb-4">
                <label class="settings-label" for="wind-threshold">Wind Alert Threshold (km/h)</label>
                <input id="wind-threshold"
                       type="number"
                       class="settings-input"
                       min="1"
                       value="@_windThresholdKmh"
                       @onchange="OnWindThresholdChanged" />
                <p class="settings-hint">Wind speeds above this value will trigger a wind alert.</p>
            </div>

            @if (_hasChanges)
            {
                <button class="settings-btn" @onclick="SaveAsync" disabled="@_isSaving">
                    @(_isSaving ? "Saving…" : "Save")
                </button>
                <button class="settings-btn settings-btn--ghost" @onclick="CancelChanges" disabled="@_isSaving">
                    Cancel
                </button>
            }

            @if (!string.IsNullOrWhiteSpace(_successMessage))
            {
                <p class="settings-hint" style="color: var(--theme-accent);">@_successMessage</p>
            }
            @if (!string.IsNullOrWhiteSpace(_errorMessage))
            {
                <p class="settings-field-error">@_errorMessage</p>
            }
        }
    </div>
</div>

@code {
    private WeatherSettingDto? _original;
    private bool _enabled;
    private int _pollIntervalMinutes;
    private TemperatureUnit _temperatureUnit;
    private double _windThresholdKmh;
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _hasChanges;
    private string? _successMessage;
    private string? _errorMessage;

    protected override async Task OnInitializedAsync()
    {
        _original = await WeatherService.LoadSettingsAsync();
        ApplyFromOriginal();
        _isLoading = false;
    }

    private void ApplyFromOriginal()
    {
        if (_original is null) return;
        _enabled = _original.Enabled;
        _pollIntervalMinutes = _original.PollIntervalMinutes;
        _temperatureUnit = _original.TemperatureUnit;
        _windThresholdKmh = _original.WindThresholdKmh;
        _hasChanges = false;
    }

    private void OnEnabledToggled(ChangeEventArgs e)
    {
        _enabled = (bool)(e.Value ?? false);
        UpdateHasChanges();
    }

    private void OnTemperatureUnitChanged(ChangeEventArgs e)
    {
        if (Enum.TryParse<TemperatureUnit>(e.Value?.ToString(), out var unit))
        {
            _temperatureUnit = unit;
            UpdateHasChanges();
        }
    }

    private void OnPollIntervalChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var minutes) && minutes >= 1)
        {
            _pollIntervalMinutes = minutes;
            UpdateHasChanges();
        }
    }

    private void OnWindThresholdChanged(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), out var threshold) && threshold >= 1)
        {
            _windThresholdKmh = threshold;
            UpdateHasChanges();
        }
    }

    private void UpdateHasChanges()
    {
        if (_original is null) { _hasChanges = false; return; }
        _hasChanges = _enabled != _original.Enabled
            || _pollIntervalMinutes != _original.PollIntervalMinutes
            || _temperatureUnit != _original.TemperatureUnit
            || _windThresholdKmh != _original.WindThresholdKmh;
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        _successMessage = null;
        _errorMessage = null;
        try
        {
            var dto = new WeatherSettingDto(
                _enabled, _pollIntervalMinutes, _temperatureUnit,
                _windThresholdKmh, _original?.ApiKey);
            _original = await WeatherService.SaveSettingsAsync(dto);
            _hasChanges = false;
            _successMessage = "Settings saved.";
        }
        catch (Exception)
        {
            _errorMessage = "Could not save settings. Please try again.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void CancelChanges() => ApplyFromOriginal();
}
```

- [ ] **Step 5: Create SettingsDisplayTab.razor**

Create `src/FamilyHQ.WebUi/Components/Settings/SettingsDisplayTab.razor`:

```razor
@using FamilyHQ.Core.DTOs
@using FamilyHQ.WebUi.Services
@inject IDisplaySettingService DisplaySettingService
@inject ISettingsApiService SettingsApiService
@inject IThemeService ThemeService

<div class="settings-section-content">
    <h2 class="settings-section__title">Display</h2>

    <!-- Appearance subsection -->
    <h3 class="settings-subsection__title">Appearance</h3>
    <div class="settings-section">
        <div class="mb-4">
            <label class="settings-label @(_opaqueSurfaces ? "settings-label--muted" : "")">
                Surface Opacity
            </label>
            <div class="flex items-center gap-3">
                <input type="range" min="0" max="100" step="5"
                       value="@(_opaqueSurfaces ? 100 : _surfaceOpacityPercent)"
                       @oninput="OnSurfaceOpacityChanged"
                       disabled="@_opaqueSurfaces"
                       class="settings-slider"
                       style="accent-color: var(--theme-accent); flex: 1;" />
                <span class="settings-slider-value">@(_opaqueSurfaces ? 100 : _surfaceOpacityPercent)%</span>
            </div>
        </div>

        <div class="form-check mb-4">
            <input type="checkbox" class="form-check-input" id="opaque-toggle"
                   checked="@_opaqueSurfaces" @onchange="OnOpaqueToggled" />
            <label class="form-check-label" for="opaque-toggle">Opaque surfaces — lock at 100%</label>
        </div>
    </div>

    <!-- Theme subsection -->
    <h3 class="settings-subsection__title">Theme</h3>
    <div class="settings-section">
        <div class="settings-toggle-row">
            <span class="settings-label">Auto-change with time of day</span>
            <input type="checkbox" class="settings-toggle-input" id="auto-theme-toggle"
                   checked="@_isAutoTheme" @onchange="OnAutoThemeToggled" />
            <label class="settings-toggle-label" for="auto-theme-toggle"></label>
        </div>

        @if (_isAutoTheme)
        {
            <p class="settings-hint">Theme changes automatically at each period boundary.</p>
        }
        else
        {
            <p class="settings-hint">Tap a theme to apply it instantly.</p>
        }

        <div class="settings-theme-grid">
            @foreach (var theme in _themes)
            {
                var isSelected = !_isAutoTheme && _selectedTheme == theme.Key;
                <div class="theme-tile theme-tile--@theme.Key @(isSelected ? "theme-tile--selected" : "") @(_isAutoTheme ? "theme-tile--readonly" : "theme-tile--selectable")"
                     data-testid="theme-tile-@theme.Key"
                     @onclick="() => OnThemeTileClicked(theme.Key)">
                    @if (isSelected)
                    {
                        <span class="theme-tile__tick" aria-hidden="true">✓</span>
                    }
                    <span class="theme-tile__name">@theme.Label</span>
                    @if (_todayTheme is not null)
                    {
                        <span class="theme-tile__time">@GetThemeTime(theme.Key)</span>
                    }
                </div>
            }
        </div>

        <div class="mb-4">
            <label class="settings-label @(!_isAutoTheme ? "settings-label--muted" : "")">
                Theme Transition Speed
            </label>
            <div class="flex items-center gap-3">
                <input type="range" min="0" max="60" step="5"
                       value="@_transitionDurationSecs"
                       @oninput="OnTransitionDurationChanged"
                       disabled="@(!_isAutoTheme)"
                       class="settings-slider"
                       style="accent-color: var(--theme-accent); flex: 1;" />
                <span class="settings-slider-value">@(_transitionDurationSecs)s</span>
            </div>
        </div>
    </div>
</div>

@code {
    private record ThemeOption(string Key, string Label);

    private static readonly ThemeOption[] _themes =
    [
        new("morning", "Morning"),
        new("daytime", "Daytime"),
        new("evening", "Evening"),
        new("night",   "Night"),
    ];

    private int _surfaceOpacityPercent = 100;
    private int _prevSurfaceOpacityPercent = 100;
    private bool _opaqueSurfaces;
    private bool _isAutoTheme = true;
    private string _selectedTheme = "daytime";
    private int _transitionDurationSecs = 15;
    private DayThemeDto? _todayTheme;

    protected override async Task OnInitializedAsync()
    {
        var settings = DisplaySettingService.CurrentSettings;
        _surfaceOpacityPercent = (int)(settings.SurfaceMultiplier * 100);
        _prevSurfaceOpacityPercent = _surfaceOpacityPercent;
        _opaqueSurfaces = settings.OpaqueSurfaces;
        _transitionDurationSecs = settings.TransitionDurationSecs;
        _isAutoTheme = settings.ThemeSelection == "auto";
        _selectedTheme = _isAutoTheme ? "daytime" : settings.ThemeSelection;

        _todayTheme = await SettingsApiService.GetTodayThemeAsync();
    }

    private string GetThemeTime(string key) => key switch
    {
        "morning" => _todayTheme!.MorningStart.ToString("HH:mm"),
        "daytime" => _todayTheme!.DaytimeStart.ToString("HH:mm"),
        "evening" => _todayTheme!.EveningStart.ToString("HH:mm"),
        "night"   => _todayTheme!.NightStart.ToString("HH:mm"),
        _ => string.Empty
    };

    private async Task OnSurfaceOpacityChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var percent))
        {
            _surfaceOpacityPercent = percent;
            _prevSurfaceOpacityPercent = percent;
            await SaveDisplayAsync();
        }
    }

    private async Task OnOpaqueToggled(ChangeEventArgs e)
    {
        _opaqueSurfaces = (bool)(e.Value ?? false);
        if (!_opaqueSurfaces)
            _surfaceOpacityPercent = _prevSurfaceOpacityPercent;
        await SaveDisplayAsync();
    }

    private async Task OnAutoThemeToggled(ChangeEventArgs e)
    {
        _isAutoTheme = (bool)(e.Value ?? false);
        var themeSelection = _isAutoTheme ? "auto" : _selectedTheme;
        await SaveDisplayAsync(themeSelection);

        if (_isAutoTheme)
            await ThemeService.ApplyCurrentPeriodAsync();
        else
            await DisplaySettingService.ApplyManualThemeAsync(_selectedTheme);
    }

    private async Task OnThemeTileClicked(string themeName)
    {
        if (_isAutoTheme) return;
        _selectedTheme = themeName;
        await DisplaySettingService.ApplyManualThemeAsync(themeName);
        await SaveDisplayAsync(themeName);
    }

    private async Task OnTransitionDurationChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var secs))
        {
            _transitionDurationSecs = secs;
            await DisplaySettingService.UpdatePropertyAsync("--theme-transition-duration", $"{secs}s");
            await SaveDisplayAsync();
        }
    }

    private async Task SaveDisplayAsync(string? themeSelectionOverride = null)
    {
        var multiplier = _surfaceOpacityPercent / 100.0;
        var themeSelection = themeSelectionOverride ?? (_isAutoTheme ? "auto" : _selectedTheme);
        await DisplaySettingService.SaveAsync(new DisplaySettingDto(
            multiplier, _opaqueSurfaces, _transitionDurationSecs, themeSelection));
    }
}
```

- [ ] **Step 6: Add CSS for the new tab layout**

Open `src/FamilyHQ.WebUi/wwwroot/css/app.css` and add the following classes. Read the existing file first, then append near the existing `.settings-*` rules:

```css
/* Settings tabbed layout */
.settings-page--tabbed {
    display: flex;
    min-height: calc(100vh - 64px);
}

.settings-tab-strip {
    width: 100px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    background: rgba(255,255,255,0.12);
    border-right: 1px solid var(--theme-border);
    padding: 12px 0;
    gap: 2px;
}

.settings-tab {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 5px;
    padding: 14px 8px;
    min-height: 62px;
    background: transparent;
    border: none;
    cursor: pointer;
    font-size: 0.7rem;
    font-weight: 500;
    color: var(--theme-text-muted);
    text-align: center;
    position: relative;
    transition: background 0.15s;
}

.settings-tab:hover {
    background: rgba(var(--theme-accent-rgb, 249,115,22), 0.06);
}

.settings-tab--active {
    background: rgba(var(--theme-accent-rgb, 249,115,22), 0.12);
    color: var(--theme-accent);
    font-weight: 700;
}

.settings-tab--active::after {
    content: '';
    position: absolute;
    right: 0;
    top: 20%;
    height: 60%;
    width: 3px;
    background: var(--theme-accent);
    border-radius: 2px 0 0 2px;
}

.settings-tab__icon {
    font-size: 1.25rem;
    line-height: 1;
}

.settings-tab__label {
    line-height: 1.2;
}

.settings-tab-content {
    flex: 1;
    overflow-y: auto;
    padding: 0;
}

.settings-section-content {
    padding: 16px 14px;
}

.settings-subsection__title {
    font-size: 0.78rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--theme-text-muted);
    opacity: 0.7;
    margin: 16px 0 10px;
    display: flex;
    align-items: center;
    gap: 6px;
}

.settings-subsection__title::after {
    content: '';
    flex: 1;
    height: 1px;
    background: var(--theme-border);
}

.settings-theme-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 8px;
    margin-bottom: 14px;
}

.theme-tile--selectable {
    cursor: pointer;
    transition: transform 0.1s;
}

.theme-tile--selectable:hover {
    transform: scale(1.02);
}

.theme-tile--readonly {
    cursor: default;
    opacity: 0.85;
}

.theme-tile--selected {
    border-color: white !important;
    box-shadow: 0 0 0 1px rgba(255,255,255,0.6);
}

.theme-tile__tick {
    position: absolute;
    top: 6px;
    right: 8px;
    font-size: 0.85rem;
}

.settings-toggle-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 12px;
    margin-bottom: 10px;
    background: rgba(255,255,255,0.12);
    border: 1px solid var(--theme-border);
    border-radius: 8px;
}

.settings-label--muted {
    opacity: 0.4;
}

/* Theme tile colour palettes */
.theme-tile--morning  { background: linear-gradient(135deg, #FFF8F0, #FFD8A8); }
.theme-tile--daytime  { background: linear-gradient(135deg, #E8F4FD, #C8E6FA); }
.theme-tile--evening  { background: linear-gradient(135deg, #2D1B4E, #4A2060); }
.theme-tile--night    { background: linear-gradient(135deg, #0A0E1A, #0F1628); }

.theme-tile--morning .theme-tile__name,
.theme-tile--morning .theme-tile__time { color: #3B1800; }
.theme-tile--daytime .theme-tile__name,
.theme-tile--daytime .theme-tile__time { color: #0F2A40; }
.theme-tile--evening .theme-tile__name { color: #F4C87A; }
.theme-tile--evening .theme-tile__time { color: #F4E8D0; }
.theme-tile--night .theme-tile__name   { color: #8BB3E8; }
.theme-tile--night .theme-tile__time   { color: #E2EAF4; }
```

- [ ] **Step 7: Build the WebUi project**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj -v minimal
```

Expected: builds cleanly.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Settings.razor \
        src/FamilyHQ.WebUi/Components/Settings/ \
        src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(settings): rework settings to tabbed layout with General/Location/Weather/Display tabs"
```

---

## Task 11: Delete WeatherSettings.razor; verify full solution build

**Files:**
- Delete: `src/FamilyHQ.WebUi/Pages/WeatherSettings.razor`

- [ ] **Step 1: Delete the WeatherSettings page**

```bash
rm src/FamilyHQ.WebUi/Pages/WeatherSettings.razor
```

- [ ] **Step 2: Build the full solution**

```bash
dotnet build FamilyHQ.sln -v minimal
```

Expected: clean build. If there are references to the deleted page, remove them.

- [ ] **Step 3: Run all unit tests**

```bash
dotnet test tests/FamilyHQ.Core.Tests/FamilyHQ.Core.Tests.csproj tests/FamilyHQ.WebApi.Tests/FamilyHQ.WebApi.Tests.csproj -v minimal
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(settings): remove WeatherSettings sub-page (content moved to Weather tab)"
```

---

## Task 12: Update E2E page objects

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Common/Pages/WeatherSettingsPage.cs`

- [ ] **Step 1: Update SettingsPage.cs**

Replace `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`:

```csharp
using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public class SettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/settings";

    public SettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    // Header
    public ILocator BackBtn => Page.Locator(".dashboard-header__back");

    // Tab navigation
    public ILocator GeneralTab  => Page.GetByTestId("tab-general");
    public ILocator LocationTab => Page.GetByTestId("tab-location");
    public ILocator WeatherTab  => Page.GetByTestId("tab-weather");
    public ILocator DisplayTab  => Page.GetByTestId("tab-display");

    // General tab
    public ILocator AccountName => Page.GetByTestId("account-name");
    public ILocator SignOutBtn  => Page.GetByTestId("sign-out-btn");

    // Location tab
    public ILocator LocationHint    => Page.Locator(".settings-hint").Filter(new() { HasText = "No location saved" });
    public ILocator LocationPill    => Page.Locator(".settings-location-pill");
    public ILocator LocationPillBadge => Page.Locator(".settings-location-pill__badge");
    public ILocator PlaceNameInput  => Page.Locator("#place-input");
    public ILocator SaveLocationBtn => Page.GetByTestId("save-location-btn");

    // Weather tab (for WeatherSteps access — same locators as WeatherSettingsPage)
    public ILocator WeatherEnabledToggle   => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect  => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput      => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput     => Page.Locator("#wind-threshold");
    public ILocator WeatherSaveBtn         => Page.Locator(".settings-btn").First;
    public ILocator WeatherCancelBtn       => Page.Locator(".settings-btn--ghost");
    public ILocator WeatherSuccessMessage  => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });

    // Display tab — theme tiles
    public ILocator MorningTile => Page.GetByTestId("theme-tile-morning");
    public ILocator DaytimeTile => Page.GetByTestId("theme-tile-daytime");
    public ILocator EveningTile => Page.GetByTestId("theme-tile-evening");
    public ILocator NightTile   => Page.GetByTestId("theme-tile-night");

    public async Task NavigateAndWaitAsync()
    {
        await NavigateAsync();
        await Page.Locator(".settings-page--tabbed").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task NavigateToLocationTabAsync()
    {
        await NavigateAndWaitAsync();
        await LocationTab.ClickAsync();
        await PlaceNameInput.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    public async Task NavigateToWeatherTabAsync()
    {
        await NavigateAndWaitAsync();
        await WeatherTab.ClickAsync();
        await WeatherEnabledToggle.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }
}
```

- [ ] **Step 2: Update WeatherSettingsPage.cs**

`WeatherSettingsPage` now delegates to `SettingsPage`'s Weather tab locators. Keep the class but update it to navigate via the settings tab instead of `/settings/weather`:

Replace `tests-e2e/FamilyHQ.E2E.Common/Pages/WeatherSettingsPage.cs`:

```csharp
using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

/// <summary>
/// Page object for the Weather settings tab within /settings.
/// Retained for backwards compatibility with WeatherSteps.
/// Delegates locators to the underlying SettingsPage.
/// </summary>
public class WeatherSettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    private readonly SettingsPage _settingsPage;

    // WeatherSettingsPage.PageUrl is /settings (weather content is now a tab within it)
    public override string PageUrl => _config.BaseUrl + "/settings";

    public WeatherSettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
        _settingsPage = new SettingsPage(page);
    }

    public ILocator EnabledToggle        => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput    => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput   => Page.Locator("#wind-threshold");
    public ILocator SaveBtn              => Page.Locator(".settings-btn").First;
    public ILocator CancelBtn            => Page.Locator(".settings-btn--ghost");
    public ILocator SuccessMessage       => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });
    public ILocator BackBtn              => Page.Locator(".dashboard-header__back");

    public async Task NavigateAndWaitAsync()
    {
        await _settingsPage.NavigateToWeatherTabAsync();
    }
}
```

- [ ] **Step 3: Build E2E projects**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Common/FamilyHQ.E2E.Common.csproj -v minimal
dotnet build tests-e2e/FamilyHQ.E2E.Steps/FamilyHQ.E2E.Steps.csproj -v minimal
```

Expected: builds cleanly.

- [ ] **Step 4: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs \
        tests-e2e/FamilyHQ.E2E.Common/Pages/WeatherSettingsPage.cs
git commit -m "test(e2e): update SettingsPage and WeatherSettingsPage for tabbed layout"
```

---

## Task 13: Update E2E feature files

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature`
- Modify: `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherSettings.feature`

- [ ] **Step 1: Update Settings.feature**

Replace `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature`:

```gherkin
Feature: Settings Page
  As an authenticated user
  I want to manage my settings
  So that I can configure my location, view my theme schedule, and sign out

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page

  Scenario: Back button returns user to the dashboard
    When I click the back button
    Then I see the calendar displayed

  Scenario: Location tab shows no saved location hint
    When I navigate to the location tab
    Then I see the no saved location hint

  Scenario: User can save a location
    When I navigate to the location tab
    And I enter "Edinburgh, Scotland" as the place name
    And I click save location
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Saved" badge on the location pill

  Scenario: Theme tiles are visible on the display tab
    When I navigate to the display tab
    Then I see the Morning theme tile with a time
    And I see the Daytime theme tile with a time
    And I see the Evening theme tile with a time
    And I see the Night theme tile with a time

  Scenario: General tab shows the signed-in username
    Then I see the username in the account section

  Scenario: User can sign out from the settings page
    When I click the sign out button on the settings page
    Then I see the "Login to Google" button
```

- [ ] **Step 2: Update WeatherSettings.feature**

Replace `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherSettings.feature`:

```gherkin
Feature: Weather Settings
  As an authenticated user
  I want to configure my weather preferences
  So that weather data displays according to my needs

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"

  Scenario: Weather settings are accessible from the settings page
    Given I am on the settings page
    When I navigate to the weather tab
    Then I see the weather enabled toggle

  Scenario: Weather settings tab shows all form fields
    When I navigate to weather settings
    Then I see the weather enabled toggle
    And I see the temperature unit selector
    And I see the poll interval input
    And I see the wind threshold input

  Scenario: Save and cancel buttons appear only after changes
    When I navigate to weather settings
    Then the save button is not visible
    When I change the temperature unit
    Then the save button is visible
    And the cancel button is visible

  Scenario: Cancel reverts unsaved changes
    When I navigate to weather settings
    And I change the temperature unit
    And I click cancel on weather settings
    Then the temperature unit shows the original value

  Scenario: Saving settings shows success message
    When I navigate to weather settings
    And I change the poll interval to 2
    And I save weather settings
    Then I see the "Settings saved." confirmation

  Scenario: Back button returns to dashboard
    When I navigate to weather settings
    And I click the back button
    Then I see the calendar displayed
```

- [ ] **Step 3: Build E2E features to check for unbound steps**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj -v minimal
```

If there are "pending step" warnings for new step patterns (`I navigate to the location tab`, `I navigate to the display tab`, `I navigate to the weather tab`), these will be implemented in Task 14.

- [ ] **Step 4: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature \
        tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherSettings.feature
git commit -m "test(e2e): update Settings and WeatherSettings features for tabbed layout"
```

---

## Task 14: Update E2E step definitions

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs`

- [ ] **Step 1: Update SettingsSteps.cs**

Replace `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs`:

```csharp
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class SettingsSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SettingsPage _settingsPage;
    private readonly DashboardPage _dashboardPage;

    public SettingsSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(page);
        _dashboardPage = new DashboardPage(page);
    }

    [Given(@"I am on the settings page")]
    public async Task GivenIAmOnTheSettingsPage()
    {
        await _settingsPage.NavigateAndWaitAsync();
    }

    [Then(@"I am on the settings page")]
    public async Task ThenIAmOnTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        page.Url.Should().EndWith("/settings");
        await Assertions.Expect(_settingsPage.AccountName).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    [When(@"I navigate to the settings page")]
    public async Task WhenINavigateToTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"I navigate to the location tab")]
    public async Task WhenINavigateToTheLocationTab()
    {
        await _settingsPage.LocationTab.ClickAsync();
        await _settingsPage.PlaceNameInput.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I navigate to the weather tab")]
    public async Task WhenINavigateToTheWeatherTab()
    {
        await _settingsPage.WeatherTab.ClickAsync();
        await _settingsPage.WeatherEnabledToggle.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I navigate to the display tab")]
    public async Task WhenINavigateToTheDisplayTab()
    {
        await _settingsPage.DisplayTab.ClickAsync();
        await _settingsPage.MorningTile.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I click the back button")]
    public async Task WhenIClickTheBackButton()
    {
        await _settingsPage.BackBtn.ClickAsync();
    }

    [When(@"I enter ""([^""]*)"" as the place name")]
    public async Task WhenIEnterAsThePlaceName(string placeName)
    {
        await _settingsPage.PlaceNameInput.FillAsync(placeName);
    }

    [When(@"I click save location")]
    public async Task WhenIClickSaveLocation()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SaveLocationBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
    }

    [When(@"I click the sign out button on the settings page")]
    public async Task WhenIClickTheSignOutButtonOnTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SignOutBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"I see the no saved location hint")]
    public async Task ThenISeeTheNoSavedLocationHint()
    {
        await _settingsPage.LocationHint.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    [Then(@"I see the location pill displaying ""([^""]*)""")]
    public async Task ThenISeeTheLocationPillDisplaying(string placeName)
    {
        await _settingsPage.LocationPill.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var text = await _settingsPage.LocationPill.InnerTextAsync();
        text.Should().Contain(placeName);
    }

    [Then(@"I see the ""([^""]*)"" badge on the location pill")]
    public async Task ThenISeeTheBadgeOnTheLocationPill(string badgeText)
    {
        var text = await _settingsPage.LocationPillBadge.InnerTextAsync();
        text.Trim().Should().Be(badgeText);
    }

    [Then(@"I see the Morning theme tile with a time")]
    public async Task ThenISeeTheMorningThemeTileWithATime()
    {
        await _settingsPage.MorningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.MorningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Morning tile should display a time");
    }

    [Then(@"I see the Daytime theme tile with a time")]
    public async Task ThenISeeTheDaytimeThemeTileWithATime()
    {
        await _settingsPage.DaytimeTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.DaytimeTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Daytime tile should display a time");
    }

    [Then(@"I see the Evening theme tile with a time")]
    public async Task ThenISeeTheEveningThemeTileWithATime()
    {
        await _settingsPage.EveningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.EveningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Evening tile should display a time");
    }

    [Then(@"I see the Night theme tile with a time")]
    public async Task ThenISeeTheNightThemeTileWithATime()
    {
        await _settingsPage.NightTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.NightTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Night tile should display a time");
    }

    [Then(@"I see the username in the account section")]
    public async Task ThenISeeTheUsernameInTheAccountSection()
    {
        await _settingsPage.AccountName.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var text = await _settingsPage.AccountName.InnerTextAsync();
        text.Trim().Should().NotBeEmpty("Account section should display the signed-in username");
    }
}
```

- [ ] **Step 2: Update WeatherSteps.cs — navigation via tab**

In `tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs`, update two methods:

**`WhenINavigateToWeatherSettings`** — use the tab-based navigation:
```csharp
[When(@"I navigate to weather settings")]
public async Task WhenINavigateToWeatherSettings()
{
    await _weatherSettingsPage.NavigateAndWaitAsync();
}
```
This already calls `_settingsPage.NavigateToWeatherTabAsync()` internally after Task 12's changes.

**`WhenIClickTheWeatherSettingsLink`** — update the step to click the Weather tab instead of a link:
```csharp
[When(@"I click the weather settings link")]
public async Task WhenIClickTheWeatherSettingsLink()
{
    await _settingsPage.WeatherTab.ClickAsync();
    var page = _scenarioContext.Get<IPage>();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
}
```

**`GivenTheUserHasASavedLocation`** — add location tab navigation before filling the input:
```csharp
[Given(@"the user has a saved location ""([^""]*)"" at ([^,]+), (.+)")]
public async Task GivenTheUserHasASavedLocation(string placeName, double lat, double lon)
{
    var suffix = Guid.NewGuid().ToString("N")[..8];
    var uniqueName = $"{placeName}_{suffix}";
    var offset = (BitConverter.ToUInt16(Guid.NewGuid().ToByteArray(), 0) % 900 + 100) / 10000.0;
    lat += offset;
    lon += offset;

    await _simulatorApi.SetLocationAsync(uniqueName, lat, lon);

    _scenarioContext["WeatherLatitude"] = lat;
    _scenarioContext["WeatherLongitude"] = lon;
    _scenarioContext["WeatherPlaceName"] = uniqueName;

    // Navigate to settings and activate the Location tab before filling the input
    await _settingsPage.NavigateToLocationTabAsync();
    await _settingsPage.PlaceNameInput.FillAsync(uniqueName);

    var page = _scenarioContext.Get<IPage>();
    await _settingsPage.SaveLocationBtn.ClickAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
    await _settingsPage.LocationPill.WaitForAsync(
        new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

    await _dashboardPage.NavigateAndWaitAsync();
}
```

**`ThenIAmOnTheWeatherSettingsPage`** — update assertion to check for the Weather tab being visible:
```csharp
[Then(@"I am on the weather settings page")]
public async Task ThenIAmOnTheWeatherSettingsPage()
{
    var page = _scenarioContext.Get<IPage>();
    page.Url.Should().Contain("/settings");
    await Assertions.Expect(_weatherSettingsPage.EnabledToggle).ToBeVisibleAsync(new() { Timeout = 30000 });
}
```

- [ ] **Step 3: Build all E2E projects**

```bash
dotnet build tests-e2e/ -v minimal
```

Expected: builds cleanly with no unbound step warnings.

- [ ] **Step 4: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs \
        tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs
git commit -m "test(e2e): update SettingsSteps and WeatherSteps for tabbed settings navigation"
```

---

## Task 15: Update architecture.md

**Files:**
- Modify: `.agent/docs/architecture.md`

- [ ] **Step 1: Update Key Entities section**

In `.agent/docs/architecture.md`, update the **Key Entities** section entries for `LocationSetting`, `DisplaySetting`, and `WeatherSetting`:

```markdown
- **LocationSetting**: Stores the user's configured location (PlaceName, Latitude, Longitude). One row per UserId; when absent, the API falls back to IP-based geolocation.
- **DisplaySetting**: Stores user display preferences (SurfaceMultiplier as `double` 0–1.0, OpaqueSurfaces as `bool`, TransitionDurationSecs as `int`, ThemeSelection as `string`). One row per UserId. ThemeSelection is `"auto"` (time-of-day transitions) or a period name (`"morning"`, `"daytime"`, `"evening"`, `"night"`).
- **WeatherSetting**: Stores weather preferences (Enabled, PollIntervalMinutes, TemperatureUnit, WindThresholdKmh). One row per UserId.
```

- [ ] **Step 2: Update Pages & Navigation section**

Replace the Pages & Navigation section:

```markdown
## Pages & Navigation
- `/` — Dashboard (Month / Day / Agenda views)
- `/settings` — Settings page — tabbed layout (General, Location, Weather, Display). Settings cog only shown when authenticated.
  - **General tab**: signed-in username, Sign Out button.
  - **Location tab**: current location with Auto/Saved badge, override input.
  - **Weather tab**: replaces the old `/settings/weather` sub-page.
  - **Display tab**: Surface Opacity (0–100%), Opaque surfaces toggle, Theme subsection (auto/manual selection, theme tiles, transition speed).
- Settings accessed via a gear icon (⚙️) in the DashboardHeader (authenticated only).
```

- [ ] **Step 3: Update API Endpoints section**

Update the display and weather endpoint descriptions to reflect auth requirement and ThemeSelection:

```markdown
- `GET  /api/settings/display` → DisplaySettingDto (SurfaceMultiplier 0–1.0, OpaqueSurfaces, TransitionDurationSecs, ThemeSelection) — requires auth; returns defaults if no row exists for the user
- `PUT  /api/settings/display` `{ surfaceMultiplier, opaqueSurfaces, transitionDurationSecs, themeSelection }` → upserts the user's DisplaySetting row; requires auth
- `GET  /api/settings/weather` → WeatherSettingDto — requires auth; scoped to current user
- `PUT  /api/settings/weather` → upserts user's weather settings; requires auth
```

- [ ] **Step 4: Commit**

```bash
git add .agent/docs/architecture.md
git commit -m "docs: update architecture.md for settings rework (per-user scoping, tabbed layout, ThemeSelection)"
```

---

## Self-Review Checklist

- [x] **Spec coverage**: All spec sections have corresponding tasks — per-user scoping (Tasks 2–8), ThemeSelection (Tasks 1, 9, 10), tabbed UI (Tasks 10–11), cog visibility (confirmed already gated in Index.razor — no task needed), E2E updates (Tasks 12–14), architecture docs (Task 15).
- [x] **No placeholders**: All steps contain actual code.
- [x] **Type consistency**: `DisplaySettingDto` positional param order is `(SurfaceMultiplier, OpaqueSurfaces, TransitionDurationSecs, ThemeSelection)` — consistent across Tasks 1, 8, 9, 10. `IDisplaySettingRepository` signatures with `(string userId, ...)` consistent across Tasks 3, 4, 8. `IWeatherSettingRepository` `GetOrCreateAsync(string userId, ct)` consistent across Tasks 3, 4, 6, 7.
- [x] **SurfaceMultiplier rescaling**: Task 5 migration rescales 0–2.0 → 0–1.0; Task 1 validator enforces max 1.0; Task 10 UI maps 0–100% → 0.0–1.0.
- [x] **ThemeService init ordering**: Task 9 swaps MainLayout init order so `DisplaySettingService` runs first; `ThemeService.InitialiseAsync()` reads `DisplaySettingService.CurrentSettings.ThemeSelection` correctly.
