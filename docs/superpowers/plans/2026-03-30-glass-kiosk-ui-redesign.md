# Glass Kiosk UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Bootstrap with custom glassmorphism-lite CSS, add Display settings (transparency + transition speed), and upgrade all UI components to the "Glass Kiosk" aesthetic.

**Architecture:** Backend adds a `DisplaySetting` entity with repository, DTO, validator, and API endpoints (GET/PUT). Frontend removes Bootstrap, rewrites `app.css` with glass surface system, migrates all Razor components to custom CSS classes, and adds a Display section to Settings. A self-hosted font (DM Sans) provides typography.

**Tech Stack:** .NET 10, Blazor WASM, EF Core / PostgreSQL, custom CSS (no framework), JS interop for real-time display property updates.

**Branch:** `feature/ui-redesign-2` (current branch)

**Spec:** `docs/superpowers/specs/2026-03-30-glass-kiosk-ui-redesign-design.md`

**Completion criteria:** A task is only complete when the solution builds successfully on Jenkins and the FamilyHQ-Dev pipeline deploys successfully. Local build/test verification is a prerequisite, but the Jenkins pipeline is the authoritative gate.

---

## File Map

### New Files
| File | Purpose |
|---|---|
| `src/FamilyHQ.Core/Models/DisplaySetting.cs` | Entity |
| `src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs` | Repository interface |
| `src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs` | DTO |
| `src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs` | FluentValidation |
| `src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs` | EF Core config |
| `src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs` | Repository |
| `src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs` | Blazor service interface |
| `src/FamilyHQ.WebUi/Services/DisplaySettingService.cs` | Blazor service |
| `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Regular.woff2` | Font 400 |
| `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Medium.woff2` | Font 500 |
| `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-SemiBold.woff2` | Font 600 |
| `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Bold.woff2` | Font 700 |
| `tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs` | Validator tests |
| `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs` | Controller tests |

### Modified Files
| File | Changes |
|---|---|
| `src/FamilyHQ.Data/FamilyHqDbContext.cs` | Add `DbSet<DisplaySetting>` |
| `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs` | Register `IDisplaySettingRepository` |
| `src/FamilyHQ.WebApi/Controllers/SettingsController.cs` | Add GET/PUT display endpoints |
| `src/FamilyHQ.WebUi/Services/ISettingsApiService.cs` | Add display methods |
| `src/FamilyHQ.WebUi/Services/SettingsApiService.cs` | Implement display methods |
| `src/FamilyHQ.WebUi/wwwroot/index.html` | Remove Bootstrap, add font preload |
| `src/FamilyHQ.WebUi/wwwroot/js/theme.js` | Add `setDisplayProperty` function |
| `src/FamilyHQ.WebUi/wwwroot/css/app.css` | Full rewrite — glass system |
| `src/FamilyHQ.WebUi/Layout/MainLayout.razor` | Remove sidebar classes, inject DisplaySettingService |
| `src/FamilyHQ.WebUi/Pages/Index.razor` | Replace Bootstrap classes |
| `src/FamilyHQ.WebUi/Pages/Settings.razor` | Add Display section, replace classes |
| `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor` | Replace Bootstrap classes |
| `src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor` | Replace with underline tabs |
| `src/FamilyHQ.WebUi/Components/Dashboard/MonthView.razor` | Replace Bootstrap + ghost nav |
| `src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor` | Replace Bootstrap + ghost nav |
| `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor` | Replace Bootstrap + ghost nav |
| `src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor` | Replace Bootstrap modal/form |
| `src/FamilyHQ.WebUi/Components/Dashboard/DayPickerModal.razor` | Replace Bootstrap modal |
| `src/FamilyHQ.WebUi/Components/Dashboard/QuickJumpModal.razor` | Replace Bootstrap modal |
| `src/FamilyHQ.WebUi/Program.cs` | Register DisplaySettingService |
| `.agent/docs/ui-design-system.md` | Update with glass variables |
| `.agent/docs/architecture.md` | Update with DisplaySetting |
| `.agent/skills/ui-theming/SKILL.md` | Update with glass rules |

### Deleted Files
| File | Reason |
|---|---|
| `src/FamilyHQ.WebUi/wwwroot/lib/bootstrap/` | Framework removed |
| `src/FamilyHQ.WebUi/Layout/MainLayout.razor.css` | Dead CSS — all layout in app.css |
| `src/FamilyHQ.WebUi/Layout/NavMenu.razor` | Unused sidebar nav |
| `src/FamilyHQ.WebUi/Layout/NavMenu.razor.css` | Accompanies NavMenu |

---

## Task 1: DisplaySetting Entity and Repository

**Files:**
- Create: `src/FamilyHQ.Core/Models/DisplaySetting.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs`
- Create: `src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs`
- Create: `src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs`
- Modify: `src/FamilyHQ.Data/FamilyHqDbContext.cs`
- Modify: `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create DisplaySetting entity**

```csharp
// src/FamilyHQ.Core/Models/DisplaySetting.cs
namespace FamilyHQ.Core.Models;

public class DisplaySetting
{
    public int Id { get; set; }
    public double SurfaceMultiplier { get; set; } = 1.0;
    public bool OpaqueSurfaces { get; set; }
    public int TransitionDurationSecs { get; set; } = 15;
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Create repository interface**

```csharp
// src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDisplaySettingRepository
{
    Task<DisplaySetting?> GetAsync(CancellationToken ct = default);
    Task<DisplaySetting> UpsertAsync(DisplaySetting setting, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create EF Core configuration**

```csharp
// src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class DisplaySettingConfiguration : IEntityTypeConfiguration<DisplaySetting>
{
    public void Configure(EntityTypeBuilder<DisplaySetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SurfaceMultiplier).IsRequired();
        builder.Property(x => x.TransitionDurationSecs).IsRequired();
        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasConversion(
                v => v.ToUniversalTime(),
                v => v);
    }
}
```

- [ ] **Step 4: Create repository implementation**

```csharp
// src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs
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

    public async Task<DisplaySetting?> GetAsync(CancellationToken ct = default)
        => await _context.DisplaySettings.FirstOrDefaultAsync(ct);

    public async Task<DisplaySetting> UpsertAsync(DisplaySetting setting, CancellationToken ct = default)
    {
        var existing = await GetAsync(ct);
        if (existing is null)
        {
            _context.DisplaySettings.Add(setting);
            await _context.SaveChangesAsync(ct);
            return setting;
        }

        existing.SurfaceMultiplier = setting.SurfaceMultiplier;
        existing.OpaqueSurfaces = setting.OpaqueSurfaces;
        existing.TransitionDurationSecs = setting.TransitionDurationSecs;
        existing.UpdatedAt = setting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
```

- [ ] **Step 5: Add DbSet to DbContext**

Add to `src/FamilyHQ.Data/FamilyHqDbContext.cs` after the `LocationSettings` line:

```csharp
public DbSet<DisplaySetting> DisplaySettings => Set<DisplaySetting>();
```

- [ ] **Step 6: Register repository in DI**

In `src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs`, add after the `ILocationSettingRepository` line:

```csharp
services.AddScoped<IDisplaySettingRepository, DisplaySettingRepository>();
```

- [ ] **Step 7: Create EF migration**

Run:
```bash
cd src/FamilyHQ.Data.PostgreSQL
dotnet ef migrations add AddDisplaySetting --startup-project ../FamilyHQ.WebApi
```
Expected: Migration files created successfully.

- [ ] **Step 8: Verify build**

Run: `dotnet build src/FamilyHQ.Data/FamilyHQ.Data.csproj`
Expected: Build succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/FamilyHQ.Core/Models/DisplaySetting.cs src/FamilyHQ.Core/Interfaces/IDisplaySettingRepository.cs src/FamilyHQ.Data/Configurations/DisplaySettingConfiguration.cs src/FamilyHQ.Data/Repositories/DisplaySettingRepository.cs src/FamilyHQ.Data/FamilyHqDbContext.cs src/FamilyHQ.Data.PostgreSQL/ServiceCollectionExtensions.cs src/FamilyHQ.Data.PostgreSQL/Migrations/
git commit -m "feat(data): add DisplaySetting entity, repository, and migration"
```

---

## Task 2: DisplaySettingDto, Validator, and Tests

**Files:**
- Create: `src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs`
- Create: `src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs`
- Create: `tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs`

- [ ] **Step 1: Create the DTO**

```csharp
// src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs
namespace FamilyHQ.Core.DTOs;

public record DisplaySettingDto(
    double SurfaceMultiplier,
    bool OpaqueSurfaces,
    int TransitionDurationSecs);
```

- [ ] **Step 2: Write the failing validator tests**

```csharp
// tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Validators;

public class DisplaySettingDtoValidatorTests
{
    private readonly DisplaySettingDtoValidator _sut = new();

    [Fact]
    public void Valid_Defaults_Passes()
    {
        var dto = new DisplaySettingDto(1.0, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void SurfaceMultiplier_OutOfRange_Fails(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SurfaceMultiplier");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void SurfaceMultiplier_AtBoundary_Passes(double value)
    {
        var dto = new DisplaySettingDto(value, false, 15);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void TransitionDurationSecs_OutOfRange_Fails(int value)
    {
        var dto = new DisplaySettingDto(1.0, false, value);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TransitionDurationSecs");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    public void TransitionDurationSecs_AtBoundary_Passes(int value)
    {
        var dto = new DisplaySettingDto(1.0, false, value);
        var result = _sut.Validate(dto);
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.Core.Tests --filter "DisplaySettingDtoValidatorTests" -v minimal`
Expected: FAIL — `DisplaySettingDtoValidator` not found.

- [ ] **Step 4: Create the validator**

```csharp
// src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs
using FamilyHQ.Core.DTOs;
using FluentValidation;

namespace FamilyHQ.Core.Validators;

public class DisplaySettingDtoValidator : AbstractValidator<DisplaySettingDto>
{
    public DisplaySettingDtoValidator()
    {
        RuleFor(x => x.SurfaceMultiplier)
            .InclusiveBetween(0.0, 2.0)
            .WithMessage("Surface multiplier must be between 0.0 and 2.0.");

        RuleFor(x => x.TransitionDurationSecs)
            .InclusiveBetween(0, 60)
            .WithMessage("Transition duration must be between 0 and 60 seconds.");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.Core.Tests --filter "DisplaySettingDtoValidatorTests" -v minimal`
Expected: All 8 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.Core/DTOs/DisplaySettingDto.cs src/FamilyHQ.Core/Validators/DisplaySettingDtoValidator.cs tests/FamilyHQ.Core.Tests/Validators/DisplaySettingDtoValidatorTests.cs
git commit -m "feat(core): add DisplaySettingDto and validator with tests"
```

---

## Task 3: Display Settings API Endpoints and Tests

**Files:**
- Modify: `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`
- Create: `tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs`

- [ ] **Step 1: Write the failing controller tests**

```csharp
// tests/FamilyHQ.WebApi.Tests/Controllers/DisplaySettingsControllerTests.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class DisplaySettingsControllerTests
{
    [Fact]
    public async Task GetDisplay_ReturnsDefaults_WhenNotSet()
    {
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DisplaySetting?)null);

        var result = await sut.GetDisplay(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DisplaySettingDto>().Subject;
        dto.SurfaceMultiplier.Should().Be(1.0);
        dto.OpaqueSurfaces.Should().BeFalse();
        dto.TransitionDurationSecs.Should().Be(15);
    }

    [Fact]
    public async Task GetDisplay_ReturnsSaved_WhenSet()
    {
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplaySetting
            {
                SurfaceMultiplier = 1.5,
                OpaqueSurfaces = true,
                TransitionDurationSecs = 30
            });

        var result = await sut.GetDisplay(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<DisplaySettingDto>().Subject;
        dto.SurfaceMultiplier.Should().Be(1.5);
        dto.OpaqueSurfaces.Should().BeTrue();
        dto.TransitionDurationSecs.Should().Be(30);
    }

    [Fact]
    public async Task PutDisplay_ValidDto_UpsertAndReturnsOk()
    {
        var (sut, _, _, _, _, _, displayRepoMock) = CreateSut();
        displayRepoMock.Setup(x => x.UpsertAsync(It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DisplaySetting ds, CancellationToken _) => ds);

        var dto = new DisplaySettingDto(1.2, false, 20);
        var result = await sut.PutDisplay(dto, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<DisplaySettingDto>();
        displayRepoMock.Verify(x => x.UpsertAsync(
            It.Is<DisplaySetting>(ds =>
                ds.SurfaceMultiplier == 1.2 &&
                ds.TransitionDurationSecs == 20 &&
                !ds.OpaqueSurfaces),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PutDisplay_InvalidDto_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _) = CreateSut();
        var dto = new DisplaySettingDto(5.0, false, 100); // out of range

        var result = await sut.PutDisplay(dto, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static (
        SettingsController sut,
        Mock<ILocationSettingRepository> locationRepoMock,
        Mock<IGeocodingService> geocodingMock,
        Mock<IDayThemeService> dayThemeServiceMock,
        Mock<IDayThemeScheduler> schedulerMock,
        Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> hubMock,
        Mock<IDisplaySettingRepository> displayRepoMock) CreateSut()
    {
        var locationRepoMock = new Mock<ILocationSettingRepository>();
        var geocodingMock = new Mock<IGeocodingService>();
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        var schedulerMock = new Mock<IDayThemeScheduler>();
        var hubMock = new Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>>();
        var loggerMock = new Mock<ILogger<SettingsController>>();
        var displayRepoMock = new Mock<IDisplaySettingRepository>();

        var sut = new SettingsController(
            locationRepoMock.Object,
            geocodingMock.Object,
            dayThemeServiceMock.Object,
            schedulerMock.Object,
            hubMock.Object,
            loggerMock.Object,
            displayRepoMock.Object);

        return (sut, locationRepoMock, geocodingMock, dayThemeServiceMock, schedulerMock, hubMock, displayRepoMock);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FamilyHQ.WebApi.Tests --filter "DisplaySettingsControllerTests" -v minimal`
Expected: FAIL — constructor mismatch (missing `IDisplaySettingRepository` parameter).

- [ ] **Step 3: Update SettingsController with display endpoints**

Add to `src/FamilyHQ.WebApi/Controllers/SettingsController.cs`:

Add `IDisplaySettingRepository` to the constructor:
```csharp
private readonly IDisplaySettingRepository _displayRepo;

public SettingsController(
    ILocationSettingRepository locationRepo,
    IGeocodingService geocodingService,
    IDayThemeService dayThemeService,
    IDayThemeScheduler scheduler,
    IHubContext<CalendarHub> hubContext,
    ILogger<SettingsController> logger,
    IDisplaySettingRepository displayRepo)
{
    _locationRepo = locationRepo;
    _geocodingService = geocodingService;
    _dayThemeService = dayThemeService;
    _scheduler = scheduler;
    _hubContext = hubContext;
    _logger = logger;
    _displayRepo = displayRepo;
}
```

Add two new action methods:
```csharp
[HttpGet("display")]
public async Task<IActionResult> GetDisplay(CancellationToken ct)
{
    var setting = await _displayRepo.GetAsync(ct);
    if (setting is null)
        return Ok(new DisplaySettingDto(1.0, false, 15));

    return Ok(new DisplaySettingDto(
        setting.SurfaceMultiplier,
        setting.OpaqueSurfaces,
        setting.TransitionDurationSecs));
}

[HttpPut("display")]
public async Task<IActionResult> PutDisplay([FromBody] DisplaySettingDto dto, CancellationToken ct)
{
    var validator = new DisplaySettingDtoValidator();
    var validation = await validator.ValidateAsync(dto, ct);
    if (!validation.IsValid)
        return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

    var setting = new DisplaySetting
    {
        SurfaceMultiplier = dto.SurfaceMultiplier,
        OpaqueSurfaces = dto.OpaqueSurfaces,
        TransitionDurationSecs = dto.TransitionDurationSecs,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    await _displayRepo.UpsertAsync(setting, ct);

    return Ok(dto);
}
```

Add the required usings at the top:
```csharp
using FamilyHQ.Core.Validators;
```

- [ ] **Step 4: Fix existing SettingsControllerTests CreateSut**

Update `tests/FamilyHQ.WebApi.Tests/Controllers/SettingsControllerTests.cs` — the `CreateSut` method needs the new parameter. Update the tuple return type and constructor call to include `Mock<IDisplaySettingRepository>`:

Change the `CreateSut` method to add:
```csharp
var displayRepoMock = new Mock<IDisplaySettingRepository>();
```
And add `displayRepoMock.Object` as the last constructor argument. Update the tuple return and destructuring.

- [ ] **Step 5: Run all tests to verify they pass**

Run: `dotnet test tests/FamilyHQ.WebApi.Tests -v minimal`
Expected: All tests pass (existing + new).

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/SettingsController.cs tests/FamilyHQ.WebApi.Tests/Controllers/
git commit -m "feat(api): add GET/PUT display settings endpoints with tests"
```

---

## Task 4: Download and Add Self-Hosted Font

**Files:**
- Create: `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Regular.woff2`
- Create: `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Medium.woff2`
- Create: `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-SemiBold.woff2`
- Create: `src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Bold.woff2`

- [ ] **Step 1: Download DM Sans WOFF2 files**

DM Sans is available from Google Fonts under the OFL licence. Download the 4 weight variants as WOFF2:

```bash
mkdir -p src/FamilyHQ.WebUi/wwwroot/fonts
# Download from google-webfonts-helper or fontsource
# Regular (400)
curl -L -o src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Regular.woff2 "https://fonts.gstatic.com/s/dmsans/v15/rP2tp2ywxg089UriI5-g4vlH9VoD8CmcqZG40F9JadbnoEwAopxhS23Yvs.woff2"
# Medium (500)
curl -L -o src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Medium.woff2 "https://fonts.gstatic.com/s/dmsans/v15/rP2tp2ywxg089UriI5-g4vlH9VoD8CmcqZG40F9JadbnoEwAkJxhS23Yvs.woff2"
# SemiBold (600)
curl -L -o src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-SemiBold.woff2 "https://fonts.gstatic.com/s/dmsans/v15/rP2tp2ywxg089UriI5-g4vlH9VoD8CmcqZG40F9JadbnoEwAfJthS23Yvs.woff2"
# Bold (700)
curl -L -o src/FamilyHQ.WebUi/wwwroot/fonts/DMSans-Bold.woff2 "https://fonts.gstatic.com/s/dmsans/v15/rP2tp2ywxg089UriI5-g4vlH9VoD8CmcqZG40F9JadbnoEwARZthS23Yvs.woff2"
```

Note: If these URLs are stale, download from https://gwfh.mranftl.com/fonts/dm-sans?subsets=latin and select weights 400, 500, 600, 700 in WOFF2 format. Place files in `src/FamilyHQ.WebUi/wwwroot/fonts/`.

- [ ] **Step 2: Verify files exist and are non-empty**

Run: `ls -la src/FamilyHQ.WebUi/wwwroot/fonts/`
Expected: 4 `.woff2` files, each 10-50KB.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/fonts/
git commit -m "feat(ui): add self-hosted DM Sans font files (WOFF2)"
```

---

## Task 5: Update theme.js with Display Property Functions

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/js/theme.js`

- [ ] **Step 1: Add setDisplayProperty function**

Replace the entire file content of `src/FamilyHQ.WebUi/wwwroot/js/theme.js` with:

```javascript
export function setTheme(period) {
    document.body.setAttribute('data-theme', period.toLowerCase());
}

export function setDisplayProperty(name, value) {
    document.body.style.setProperty(name, value);
}

export function removeDisplayProperty(name) {
    document.body.style.removeProperty(name);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/js/theme.js
git commit -m "feat(ui): add display property JS interop functions"
```

---

## Task 6: Blazor DisplaySettingService and API Client Updates

**Files:**
- Create: `src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs`
- Create: `src/FamilyHQ.WebUi/Services/DisplaySettingService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/ISettingsApiService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/SettingsApiService.cs`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

- [ ] **Step 1: Add display methods to ISettingsApiService**

Add to `src/FamilyHQ.WebUi/Services/ISettingsApiService.cs`:

```csharp
Task<DisplaySettingDto> GetDisplayAsync();
Task<DisplaySettingDto> SaveDisplayAsync(DisplaySettingDto dto);
```

- [ ] **Step 2: Implement display methods in SettingsApiService**

Add to `src/FamilyHQ.WebUi/Services/SettingsApiService.cs`:

```csharp
public async Task<DisplaySettingDto> GetDisplayAsync()
{
    return (await _httpClient.GetFromJsonAsync<DisplaySettingDto>("api/settings/display"))!;
}

public async Task<DisplaySettingDto> SaveDisplayAsync(DisplaySettingDto dto)
{
    var response = await _httpClient.PutAsJsonAsync("api/settings/display", dto);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<DisplaySettingDto>())!;
}
```

- [ ] **Step 3: Create IDisplaySettingService interface**

```csharp
// src/FamilyHQ.WebUi/Services/IDisplaySettingService.cs
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface IDisplaySettingService
{
    Task InitialiseAsync();
    Task UpdatePropertyAsync(string cssPropertyName, string value);
    Task SaveAsync(DisplaySettingDto dto);
    DisplaySettingDto CurrentSettings { get; }
}
```

- [ ] **Step 4: Create DisplaySettingService implementation**

```csharp
// src/FamilyHQ.WebUi/Services/DisplaySettingService.cs
using FamilyHQ.Core.DTOs;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class DisplaySettingService : IDisplaySettingService, IAsyncDisposable
{
    private readonly ISettingsApiService _settingsApi;
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;

    public DisplaySettingDto CurrentSettings { get; private set; } =
        new(1.0, false, 15);

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

    private async Task ApplyAllPropertiesAsync()
    {
        var module = await GetModuleAsync();

        var multiplier = CurrentSettings.OpaqueSurfaces ? "100" : CurrentSettings.SurfaceMultiplier.ToString("F2");
        await module.InvokeVoidAsync("setDisplayProperty", "--user-surface-multiplier", multiplier);

        var duration = $"{CurrentSettings.TransitionDurationSecs}s";
        await module.InvokeVoidAsync("setDisplayProperty", "--theme-transition-duration", duration);
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

- [ ] **Step 5: Register service in Program.cs**

Add to `src/FamilyHQ.WebUi/Program.cs` after the `ISettingsApiService` registration:

```csharp
builder.Services.AddScoped<IDisplaySettingService, DisplaySettingService>();
```

- [ ] **Step 6: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/ src/FamilyHQ.WebUi/Program.cs
git commit -m "feat(ui): add DisplaySettingService for real-time display property control"
```

---

## Task 7: Rewrite app.css — Glass Surface System

This is the largest task. The entire `app.css` is rewritten to remove Bootstrap dependencies and implement the glass surface system.

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

- [ ] **Step 1: Rewrite the complete app.css**

Replace the **entire** contents of `src/FamilyHQ.WebUi/wwwroot/css/app.css`. The new file follows the structure defined in the spec: `@font-face` → `@property` → reset → themes → utilities → layers → components → spinner.

The full CSS file will be approximately 900-1100 lines. Key sections to implement:

**Section 1 — @font-face (DM Sans)**
```css
@font-face {
    font-family: 'DM Sans';
    src: url('../fonts/DMSans-Regular.woff2') format('woff2');
    font-weight: 400;
    font-style: normal;
    font-display: swap;
}
@font-face {
    font-family: 'DM Sans';
    src: url('../fonts/DMSans-Medium.woff2') format('woff2');
    font-weight: 500;
    font-style: normal;
    font-display: swap;
}
@font-face {
    font-family: 'DM Sans';
    src: url('../fonts/DMSans-SemiBold.woff2') format('woff2');
    font-weight: 600;
    font-style: normal;
    font-display: swap;
}
@font-face {
    font-family: 'DM Sans';
    src: url('../fonts/DMSans-Bold.woff2') format('woff2');
    font-weight: 700;
    font-style: normal;
    font-display: swap;
}
```

**Section 2 — @property registrations** — keep all existing 11 colour properties, add 7 new glass properties:
```css
/* Existing theme colour properties — keep as-is */
@property --theme-bg-start    { syntax: '<color>'; inherits: true; initial-value: #FFF8F0; }
/* ... all 11 existing ... */

/* New glass properties */
@property --theme-glass-border    { syntax: '<color>'; inherits: true; initial-value: rgba(255,255,255,0.6); }
@property --theme-glass-ring      { syntax: '<color>'; inherits: true; initial-value: rgba(124,58,0,0.06); }
@property --theme-glass-shadow    { syntax: '<color>'; inherits: true; initial-value: rgba(0,0,0,0.04); }
@property --theme-glass-highlight { syntax: '<color>'; inherits: true; initial-value: rgba(255,255,255,0.7); }
@property --theme-glass-divider   { syntax: '<color>'; inherits: true; initial-value: rgba(124,58,0,0.1); }
```

**Section 3 — CSS reset**
```css
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
html, body {
    height: 100%;
    font-family: 'DM Sans', system-ui, -apple-system, sans-serif;
    color: var(--theme-text);
    transition: color var(--theme-transition-duration, 15s) ease-in-out;
    -webkit-font-smoothing: antialiased;
}
```

**Section 4 — Theme blocks** — extend each existing block with glass variables:
```css
[data-theme="morning"] {
    /* existing 11 variables unchanged */
    --theme-glass-border:    rgba(255,255,255,0.6);
    --theme-glass-ring:      rgba(124,58,0,0.06);
    --theme-glass-shadow:    rgba(0,0,0,0.04);
    --theme-glass-highlight: rgba(255,255,255,0.7);
    --theme-glass-divider:   rgba(124,58,0,0.1);
    --theme-surface-opacity: 0.55;
}
/* Repeat for daytime, evening, night with spec values */
```

**Section 5 — Layout utilities** — custom replacements for Bootstrap utilities:
```css
.flex { display: flex; }
.flex-col { display: flex; flex-direction: column; }
.items-center { align-items: center; }
.justify-between { justify-content: space-between; }
.justify-center { justify-content: center; }
.text-center { text-align: center; }
.hidden { display: none; }
.block { display: block; }
.sr-only { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); border: 0; }
.text-danger { color: #ef4444; }
/* Spacing: mb-1 through mb-5, mt-1 through mt-5, etc. */
.mb-1 { margin-bottom: 4px; } .mb-2 { margin-bottom: 8px; }
.mb-3 { margin-bottom: 16px; } .mb-4 { margin-bottom: 24px; }
.mb-5 { margin-bottom: 40px; }
/* ... same pattern for mt, ms, px, py, p, gap ... */
.w-full { width: 100%; }
.grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
```

**Section 6 — Glass surface mixin pattern** (using a shared class):
```css
.glass-surface {
    background: rgba(255, 255, 255, calc(var(--theme-surface-opacity, 0.55) * var(--user-surface-multiplier, 1)));
    border: 1px solid var(--theme-glass-border);
    border-radius: 8px;
    box-shadow:
        0 0 0 1px var(--theme-glass-ring),
        0 8px 24px var(--theme-glass-shadow),
        inset 0 1px 0 var(--theme-glass-highlight);
    transition:
        background-color var(--theme-transition-duration, 15s) ease-in-out,
        border-color var(--theme-transition-duration, 15s) ease-in-out,
        box-shadow var(--theme-transition-duration, 15s) ease-in-out;
}
```

**Section 7 — Buttons:**
```css
.btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-height: 48px;
    padding: 10px 20px;
    font-size: 14px;
    font-family: inherit;
    font-weight: 500;
    border-radius: 6px;
    cursor: pointer;
    border: none;
    transition:
        background-color var(--theme-transition-duration, 15s) ease-in-out,
        color var(--theme-transition-duration, 15s) ease-in-out,
        border-color var(--theme-transition-duration, 15s) ease-in-out;
}
.btn:active { filter: brightness(0.95); }
.btn-primary {
    background: var(--theme-accent);
    color: var(--theme-on-accent);
    font-weight: 600;
    box-shadow: 0 0 12px color-mix(in srgb, var(--theme-accent) 20%, transparent);
}
.btn-secondary, .btn-glass {
    background: rgba(255, 255, 255, calc(var(--theme-surface-opacity, 0.55) * var(--user-surface-multiplier, 1)));
    color: var(--theme-text);
    border: 1px solid var(--theme-glass-border);
    box-shadow: inset 0 1px 0 var(--theme-glass-highlight);
}
.btn-ghost {
    background: transparent;
    color: var(--theme-text-muted);
    padding: 10px;
    min-height: auto;
}
.btn-danger {
    background: transparent;
    color: #ef4444;
    border: 1px solid rgba(239, 68, 68, 0.3);
}
.btn-lg { padding: 14px 28px; font-size: 16px; }
.btn-sm { padding: 6px 12px; font-size: 12px; min-height: 36px; }
.btn-close {
    background: transparent;
    border: none;
    color: var(--theme-text-muted);
    font-size: 20px;
    cursor: pointer;
    padding: 8px;
    line-height: 1;
}
.btn-group { display: inline-flex; gap: 4px; }
```

**Section 8 — View tabs (clean underline):**
```css
.view-tabs {
    display: flex;
    border-bottom: 2px solid var(--theme-glass-divider);
    list-style: none;
    transition: border-color var(--theme-transition-duration, 15s) ease-in-out;
}
.view-tab {
    padding: 12px 20px;
    font-size: 14px;
    font-weight: 500;
    color: var(--theme-text-muted);
    background: none;
    border: none;
    border-bottom: 2px solid transparent;
    margin-bottom: -2px;
    cursor: pointer;
    min-height: 48px;
    transition:
        color var(--theme-transition-duration, 15s) ease-in-out,
        border-color var(--theme-transition-duration, 15s) ease-in-out;
}
.view-tab.active {
    font-weight: 600;
    color: var(--theme-text);
    border-bottom-color: var(--theme-accent);
}
```

**Section 9 — Modals:**
```css
.modal-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.4);
    z-index: 100;
    display: flex;
    align-items: center;
    justify-content: center;
}
.modal-dialog { width: 100%; max-width: 500px; padding: 16px; }
.modal-sm .modal-dialog { max-width: 360px; }
.modal-content {
    background: rgba(255, 255, 255, calc(var(--theme-surface-opacity, 0.55) * var(--user-surface-multiplier, 1)));
    border: 1px solid var(--theme-glass-border);
    border-radius: 8px;
    box-shadow:
        0 0 0 1px var(--theme-glass-ring),
        0 16px 48px var(--theme-glass-shadow),
        inset 0 1px 0 var(--theme-glass-highlight);
    color: var(--theme-text);
}
.modal-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px 20px;
    border-bottom: 1px solid var(--theme-glass-divider);
}
.modal-title { font-size: 18px; font-weight: 600; }
.modal-body { padding: 20px; }
.modal-footer {
    display: flex;
    justify-content: space-between;
    padding: 16px 20px;
    border-top: 1px solid var(--theme-glass-divider);
    gap: 8px;
}
```

**Section 10 — Forms:**
```css
.form-label {
    display: block;
    font-size: 13px;
    font-weight: 500;
    color: var(--theme-text-muted);
    margin-bottom: 6px;
}
.form-input {
    width: 100%;
    background: transparent;
    border: 1px solid var(--theme-glass-border);
    border-radius: 6px;
    color: var(--theme-text);
    padding: 10px 14px;
    font-size: 14px;
    font-family: inherit;
    transition:
        border-color var(--theme-transition-duration, 15s) ease-in-out,
        color var(--theme-transition-duration, 15s) ease-in-out;
}
.form-input:focus {
    outline: none;
    border-color: var(--theme-accent);
    box-shadow: 0 0 0 2px color-mix(in srgb, var(--theme-accent) 20%, transparent);
}
.form-check { display: flex; align-items: center; gap: 8px; }
.form-check-input { accent-color: var(--theme-accent); width: 20px; height: 20px; }
.form-check-label { font-size: 14px; color: var(--theme-text); }
```

**Section 11 — Alerts:**
```css
.alert {
    padding: 12px 16px;
    border-radius: 6px;
    font-size: 14px;
}
.alert-warning {
    background: rgba(234, 179, 8, 0.15);
    color: var(--theme-text);
    border: 1px solid rgba(234, 179, 8, 0.3);
}
.alert-danger {
    background: rgba(239, 68, 68, 0.15);
    color: #ef4444;
    border: 1px solid rgba(239, 68, 68, 0.3);
}
```

**Section 12 — Spinner:**
```css
.spinner {
    display: inline-block;
    width: 24px;
    height: 24px;
    border: 3px solid var(--theme-glass-divider);
    border-top-color: var(--theme-accent);
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
}
.spinner-sm { width: 16px; height: 16px; border-width: 2px; }
@keyframes spin { to { transform: rotate(360deg); } }
```

**Remaining sections:** Carry forward the existing calendar-specific styles (month-table, day-view, agenda-view, chip-selector, dashboard-header, settings-page) but update all colours to use theme variables, replace all `45s` transitions with `var(--theme-transition-duration, 15s)`, and remove any Bootstrap overrides. The structural layout CSS (grid, positioning, sizing) stays the same.

- [ ] **Step 2: Verify no syntax errors**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded (CSS is not compiled, but this verifies the project still builds).

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(ui): rewrite app.css with glass surface system and custom utilities"
```

---

## Task 8: Update index.html — Remove Bootstrap, Add Font Preload

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/index.html`

- [ ] **Step 1: Remove Bootstrap link and add font preload**

In `src/FamilyHQ.WebUi/wwwroot/index.html`:

Remove this line:
```html
<link rel="stylesheet" href="lib/bootstrap/dist/css/bootstrap.min.css" />
```

Add font preloads before the `app.css` link:
```html
<link rel="preload" href="fonts/DMSans-Regular.woff2" as="font" type="font/woff2" crossorigin />
<link rel="preload" href="fonts/DMSans-Medium.woff2" as="font" type="font/woff2" crossorigin />
<link rel="preload" href="fonts/DMSans-SemiBold.woff2" as="font" type="font/woff2" crossorigin />
<link rel="preload" href="fonts/DMSans-Bold.woff2" as="font" type="font/woff2" crossorigin />
```

- [ ] **Step 2: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/index.html
git commit -m "feat(ui): remove Bootstrap CSS, add DM Sans font preloads"
```

---

## Task 9: Remove NavMenu and Clean Up MainLayout

**Files:**
- Delete: `src/FamilyHQ.WebUi/Layout/NavMenu.razor`
- Delete: `src/FamilyHQ.WebUi/Layout/NavMenu.razor.css`
- Delete: `src/FamilyHQ.WebUi/Layout/MainLayout.razor.css`
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`

- [ ] **Step 1: Delete NavMenu files and MainLayout scoped CSS**

```bash
rm src/FamilyHQ.WebUi/Layout/NavMenu.razor
rm src/FamilyHQ.WebUi/Layout/NavMenu.razor.css
rm src/FamilyHQ.WebUi/Layout/MainLayout.razor.css
```

- [ ] **Step 2: Update MainLayout.razor**

Replace the entire contents of `src/FamilyHQ.WebUi/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase
@using FamilyHQ.WebUi.Services
@inject IThemeService ThemeService
@inject IDisplaySettingService DisplaySettingService

<div class="page">
    @Body
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ThemeService.InitialiseAsync();
        await DisplaySettingService.InitialiseAsync();
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded. If NavMenu is referenced elsewhere (e.g. _Imports.razor), remove the reference.

- [ ] **Step 4: Commit**

```bash
git add -A src/FamilyHQ.WebUi/Layout/
git commit -m "refactor(ui): remove NavMenu sidebar and dead scoped CSS"
```

---

## Task 10: Migrate DashboardHeader Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor`

- [ ] **Step 1: Read current DashboardHeader.razor**

Read `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor` to see exact current markup and Bootstrap classes.

- [ ] **Step 2: Replace Bootstrap classes with custom CSS**

Replace all Bootstrap classes (`d-flex`, `align-items-center`, `gap-2`) with their custom equivalents (`flex`, `items-center`, `gap-2`). The settings gear button should use the `glass-surface` class with an 8px border-radius and 40×40px size (48px touch target with padding).

Example replacement pattern:
- `class="d-flex align-items-center"` → `class="flex items-center"`
- Settings button: add `glass-surface` class, ensure 48px touch target

- [ ] **Step 3: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor
git commit -m "refactor(ui): migrate DashboardHeader to custom CSS classes"
```

---

## Task 11: Migrate DashboardTabs Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor`

- [ ] **Step 1: Replace Bootstrap tabs with clean underline tabs**

Replace the entire markup of `src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor`:

```razor
<div class="view-tabs mb-4">
    <button data-testid="month-tab"
            class="view-tab @(CurrentView == DashboardView.Month ? "active" : "")"
            @onclick="() => OnViewChanged.InvokeAsync(DashboardView.Month)">Month View</button>
    <button data-testid="agenda-tab"
            class="view-tab @(CurrentView == DashboardView.MonthAgenda ? "active" : "")"
            @onclick="() => OnViewChanged.InvokeAsync(DashboardView.MonthAgenda)">Agenda</button>
    <button data-testid="day-tab"
            class="view-tab @(CurrentView == DashboardView.Day ? "active" : "")"
            @onclick="() => OnViewChanged.InvokeAsync(DashboardView.Day)">Day View</button>
</div>
```

Note: Changed from `<ul>/<li>` to plain `<div>/<button>` — no Bootstrap nav structure needed.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor
git commit -m "refactor(ui): replace Bootstrap tabs with clean underline view-tabs"
```

---

## Task 12: Migrate MonthView Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/MonthView.razor`

- [ ] **Step 1: Read current MonthView.razor**

Read the full file to map all Bootstrap classes.

- [ ] **Step 2: Replace navigation with ghost arrows**

Replace the navigation header section. The pattern:
- `class="d-flex justify-content-between align-items-center mb-4"` → `class="flex justify-between items-center mb-4"`
- Remove `btn-group` wrapper around prev/next
- Prev button: `class="btn btn-outline-primary"` → `class="btn btn-ghost"` with `‹` chevron text
- Next button: same pattern with `›`
- Today button: `class="btn btn-primary"` stays as `class="btn btn-primary"`
- "Add event" button: `class="btn btn-primary ms-3"` → `class="btn btn-primary ms-3"`

- [ ] **Step 3: Replace other Bootstrap classes**

Throughout the component:
- `class="text-muted"` → `style="color: var(--theme-text-muted)"`
- `class="bg-light"` → remove (use theme-appropriate opacity)
- `class="btn btn-sm btn-link p-0 shadow-none"` → `class="btn btn-ghost btn-sm"`
- `class="text-center"` → `class="text-center"`
- `class="mt-1"` / `class="mt-2"` → `class="mt-1"` / `class="mt-2"`

- [ ] **Step 4: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/MonthView.razor
git commit -m "refactor(ui): migrate MonthView to glass CSS with ghost nav arrows"
```

---

## Task 13: Migrate DayView Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor`

- [ ] **Step 1: Read current DayView.razor and replace Bootstrap classes**

Same navigation pattern as MonthView:
- `d-flex justify-content-between align-items-center mb-4` → `flex justify-between items-center mb-4`
- `btn-group` → remove wrapper, use `btn-group` custom class or inline flex
- Prev/Next → `btn btn-ghost` with chevrons
- Today → `btn btn-primary`
- `fw-bold` → `style="font-weight: 700"` or add `.fw-bold { font-weight: 700; }` to utilities
- `ms-3` → `ms-3`

- [ ] **Step 2: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor
git commit -m "refactor(ui): migrate DayView to glass CSS with ghost nav arrows"
```

---

## Task 14: Migrate AgendaView Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor`

- [ ] **Step 1: Read and replace Bootstrap classes**

Same navigation pattern as MonthView and DayView. Apply identical replacements.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor
git commit -m "refactor(ui): migrate AgendaView to glass CSS with ghost nav arrows"
```

---

## Task 15: Migrate EventModal Component

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor`

- [ ] **Step 1: Read current EventModal.razor**

This component uses the most Bootstrap classes. Map each one.

- [ ] **Step 2: Replace modal structure**

Replace Bootstrap modal markup:
```
Before: <div class="modal fade show d-block"> <div class="modal-dialog modal-dialog-centered">
After:  <div class="modal-backdrop"> <div class="modal-dialog">
```

Replace form classes:
- `form-label` → `form-label` (same name, custom definition)
- `form-control` → `form-input`
- `form-check` → `form-check`
- `form-check-input` → `form-check-input`
- `form-check-label` → `form-check-label`

Replace layout classes:
- `row` + `col-6` → `grid-2`
- `mb-3` → `mb-3`
- `d-flex justify-content-between` → `flex justify-between`

Replace button classes:
- `btn btn-primary` → `btn btn-primary`
- `btn btn-secondary` → `btn btn-secondary`
- `btn btn-outline-danger` → `btn btn-danger`
- `btn-close` → `btn-close`

Replace utility classes:
- `text-danger mt-1` → `text-danger mt-1`
- `alert alert-danger p-2 mb-0` → `alert alert-danger` (adjust padding in CSS)
- `spinner-border spinner-border-sm` → `spinner spinner-sm`
- `visually-hidden` → `sr-only`

- [ ] **Step 3: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor
git commit -m "refactor(ui): migrate EventModal to glass CSS modal and form system"
```

---

## Task 16: Migrate DayPickerModal and QuickJumpModal

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DayPickerModal.razor`
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/QuickJumpModal.razor`

- [ ] **Step 1: Read both modals**

- [ ] **Step 2: Migrate DayPickerModal**

Replace Bootstrap modal structure → custom modal markup. Same pattern as EventModal:
- `modal fade show d-block` → `modal-backdrop`
- `modal-dialog modal-sm modal-dialog-centered` → `modal-sm modal-dialog`
- `modal-content/header/body` → custom versions
- `form-control` → `form-input`
- `btn btn-primary w-100` → `btn btn-primary w-full`
- `btn btn-outline-secondary` → `btn btn-secondary`
- `btn-close` → `btn-close`
- `pb-2` → custom padding
- `text-center` → `text-center`
- `mb-3`, `mb-2` → `mb-3`, `mb-2`

- [ ] **Step 3: Migrate QuickJumpModal**

Same modal structure replacement, plus:
- `border-0 pb-0` / `border-0 pt-0` → remove (modal-header/footer borders handled by custom CSS, add modifier class if needed)
- `d-flex justify-content-between align-items-center mb-3` → `flex justify-between items-center mb-3`
- `btn btn-sm btn-outline-secondary` → `btn btn-sm btn-secondary`
- `fs-5 fw-bold` → `style="font-size: 18px; font-weight: 700"`
- `row g-2` / `col-4` → `style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px;"`
- `btn btn-primary w-100` / `btn btn-outline-primary w-100` → `btn btn-primary w-full` / `btn btn-secondary w-full`
- `justify-content-center` → `justify-center`
- `btn btn-link text-decoration-none` → `btn btn-ghost`

- [ ] **Step 4: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DayPickerModal.razor src/FamilyHQ.WebUi/Components/Dashboard/QuickJumpModal.razor
git commit -m "refactor(ui): migrate DayPickerModal and QuickJumpModal to glass CSS"
```

---

## Task 17: Migrate Index.razor (Dashboard Page)

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Read current Index.razor and replace Bootstrap classes**

Key replacements:
- `d-flex flex-column align-items-center justify-content-center` → `flex flex-col items-center justify-center`
- `py-5` → `py-5`
- `mb-4` → `mb-4`
- `text-muted` → replace with `style="color: var(--theme-text-muted)"` or a class
- `btn btn-success btn-lg` → `btn btn-primary btn-lg` (there's no success variant in the glass system — login button uses primary accent)
- `spinner-border text-primary` → `spinner`
- `alert alert-warning` → `alert alert-warning`
- `visually-hidden` → `sr-only`
- `mt-2` → `mt-2`

- [ ] **Step 2: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor(ui): migrate Index page to glass CSS utilities"
```

---

## Task 18: Add Display Section to Settings Page

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor`

- [ ] **Step 1: Add Display section between Theme Schedule and Account**

Add the following section to `src/FamilyHQ.WebUi/Pages/Settings.razor`, between the Theme Schedule and Account sections. Also inject `IDisplaySettingService`:

Add inject:
```razor
@inject IDisplaySettingService DisplaySettingService
```

Add the Display section markup:
```razor
<h2 class="settings-section__title">Display</h2>
<div class="settings-section">
    <div class="mb-4">
        <label class="settings-label">Surface Transparency</label>
        <div class="flex items-center gap-3">
            <input type="range" min="0" max="200" step="10"
                   value="@(_surfaceMultiplierPercent)"
                   @oninput="OnSurfaceMultiplierChanged"
                   disabled="@_opaqueSurfaces"
                   class="settings-slider"
                   style="accent-color: var(--theme-accent); flex: 1;" />
            <span class="settings-slider-value">@(_surfaceMultiplierPercent)%</span>
        </div>
    </div>

    <div class="form-check mb-4">
        <input type="checkbox" class="form-check-input" id="opaque-toggle"
               checked="@_opaqueSurfaces" @onchange="OnOpaqueToggled" />
        <label class="form-check-label" for="opaque-toggle">Opaque surfaces (disable transparency)</label>
    </div>

    <div>
        <label class="settings-label">Theme Transition Speed</label>
        <div class="flex items-center gap-3">
            <input type="range" min="0" max="60" step="5"
                   value="@_transitionDurationSecs"
                   @oninput="OnTransitionDurationChanged"
                   class="settings-slider"
                   style="accent-color: var(--theme-accent); flex: 1;" />
            <span class="settings-slider-value">@(_transitionDurationSecs)s</span>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Add code-behind fields and methods**

Add to the `@code` block:

```csharp
private int _surfaceMultiplierPercent = 100;
private bool _opaqueSurfaces;
private int _transitionDurationSecs = 15;

// Add to OnInitializedAsync, after existing code:
var displaySettings = DisplaySettingService.CurrentSettings;
_surfaceMultiplierPercent = (int)(displaySettings.SurfaceMultiplier * 100);
_opaqueSurfaces = displaySettings.OpaqueSurfaces;
_transitionDurationSecs = displaySettings.TransitionDurationSecs;

// New methods:
private async Task OnSurfaceMultiplierChanged(ChangeEventArgs e)
{
    if (int.TryParse(e.Value?.ToString(), out var percent))
    {
        _surfaceMultiplierPercent = percent;
        var multiplier = percent / 100.0;
        await DisplaySettingService.UpdatePropertyAsync("--user-surface-multiplier", multiplier.ToString("F2"));
        await DisplaySettingService.SaveAsync(new DisplaySettingDto(multiplier, _opaqueSurfaces, _transitionDurationSecs));
    }
}

private async Task OnOpaqueToggled(ChangeEventArgs e)
{
    _opaqueSurfaces = (bool)(e.Value ?? false);
    var multiplier = _surfaceMultiplierPercent / 100.0;
    if (_opaqueSurfaces)
    {
        await DisplaySettingService.UpdatePropertyAsync("--user-surface-multiplier", "100");
    }
    else
    {
        await DisplaySettingService.UpdatePropertyAsync("--user-surface-multiplier", multiplier.ToString("F2"));
    }
    await DisplaySettingService.SaveAsync(new DisplaySettingDto(multiplier, _opaqueSurfaces, _transitionDurationSecs));
}

private async Task OnTransitionDurationChanged(ChangeEventArgs e)
{
    if (int.TryParse(e.Value?.ToString(), out var secs))
    {
        _transitionDurationSecs = secs;
        await DisplaySettingService.UpdatePropertyAsync("--theme-transition-duration", $"{secs}s");
        var multiplier = _surfaceMultiplierPercent / 100.0;
        await DisplaySettingService.SaveAsync(new DisplaySettingDto(multiplier, _opaqueSurfaces, _transitionDurationSecs));
    }
}
```

- [ ] **Step 3: Add slider CSS to app.css**

Add to the Settings section of `app.css`:
```css
.settings-slider {
    -webkit-appearance: none;
    appearance: none;
    height: 6px;
    border-radius: 3px;
    background: var(--theme-glass-divider);
    outline: none;
}
.settings-slider::-webkit-slider-thumb {
    -webkit-appearance: none;
    appearance: none;
    width: 24px;
    height: 24px;
    border-radius: 50%;
    background: var(--theme-accent);
    cursor: pointer;
    border: 2px solid var(--theme-on-accent);
    box-shadow: 0 0 8px color-mix(in srgb, var(--theme-accent) 30%, transparent);
}
.settings-slider-value {
    font-size: 14px;
    font-weight: 600;
    color: var(--theme-text);
    min-width: 50px;
    text-align: right;
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Settings.razor src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(ui): add Display settings section with transparency and transition controls"
```

---

## Task 19: Delete Bootstrap Files

**Files:**
- Delete: `src/FamilyHQ.WebUi/wwwroot/lib/bootstrap/`

- [ ] **Step 1: Remove Bootstrap directory**

```bash
rm -rf src/FamilyHQ.WebUi/wwwroot/lib/bootstrap/
```

- [ ] **Step 2: Check for any remaining Bootstrap references**

Run: `grep -r "bootstrap" src/FamilyHQ.WebUi/ --include="*.razor" --include="*.html" --include="*.css" --include="*.cs"`
Expected: No results (all references should have been removed in prior tasks).

- [ ] **Step 3: Verify build**

Run: `dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -A src/FamilyHQ.WebUi/wwwroot/lib/
git commit -m "chore(ui): remove Bootstrap CSS framework files"
```

---

## Task 20: Full Build and Test Verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build FamilyHQ.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test FamilyHQ.sln -v minimal`
Expected: All tests pass. If any fail due to the SettingsController constructor change, fix the test's CreateSut method (Task 3 Step 4).

- [ ] **Step 3: Fix any remaining issues**

If any tests fail or build errors occur, fix them.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve build and test issues from Bootstrap removal"
```

---

## Task 21: Update Documentation

**Files:**
- Modify: `.agent/docs/ui-design-system.md`
- Modify: `.agent/docs/architecture.md`
- Modify: `.agent/skills/ui-theming/SKILL.md`

- [ ] **Step 1: Update ui-design-system.md**

Add to `.agent/docs/ui-design-system.md`:

- Add "Glass Surface System" section documenting the new `--theme-glass-*` variables and `--user-surface-multiplier`
- Update the "CSS Architecture" section to remove Bootstrap references
- Change all `45s` references to `var(--theme-transition-duration, 15s)` with note about configurable default
- Add "Typography" section documenting DM Sans font family
- Add "Shape Language" section documenting the 6-8px border radius system
- Add "Display Settings" section documenting the transparency and transition controls
- Update "Adding a New Themed Component" checklist to reference glass surface pattern

- [ ] **Step 2: Update architecture.md**

Add to `.agent/docs/architecture.md`:

- Add `DisplaySetting` to Key Entities section
- Add `GET /api/settings/display` and `PUT /api/settings/display` to API Endpoints
- Add `DisplaySettingService` to the UI services description
- Update UI Layer Architecture section to note Bootstrap has been removed
- Update CSS framework description to "Custom CSS with glass surface system"

- [ ] **Step 3: Update ui-theming SKILL.md**

Update `.agent/skills/ui-theming/SKILL.md`:

- Add glass surface variables to the Quick Reference table (`--theme-glass-border`, `--theme-glass-ring`, etc.)
- Update the checklist: change `45s` to `var(--theme-transition-duration, 15s)`
- Add note: "No Bootstrap — all styling is custom CSS in `app.css`"
- Add glass surface pattern: "Use `.glass-surface` class or replicate the pattern for cards/panels"
- Add note about `--user-surface-multiplier` for surface transparency
- Add utility class reference table

- [ ] **Step 4: Commit**

```bash
git add .agent/docs/ui-design-system.md .agent/docs/architecture.md .agent/skills/ui-theming/SKILL.md
git commit -m "docs: update design system, architecture, and theming skill for glass kiosk redesign"
```

---

## Summary

| Task | Description | Estimated Steps |
|---|---|---|
| 1 | DisplaySetting entity + repository + migration | 9 |
| 2 | DisplaySettingDto + validator + tests | 6 |
| 3 | API endpoints + controller tests | 6 |
| 4 | Download DM Sans font files | 3 |
| 5 | Update theme.js | 2 |
| 6 | Blazor DisplaySettingService + API client | 7 |
| 7 | Rewrite app.css (glass surface system) | 3 |
| 8 | Update index.html | 2 |
| 9 | Remove NavMenu + clean MainLayout | 4 |
| 10 | Migrate DashboardHeader | 4 |
| 11 | Migrate DashboardTabs | 3 |
| 12 | Migrate MonthView | 5 |
| 13 | Migrate DayView | 3 |
| 14 | Migrate AgendaView | 3 |
| 15 | Migrate EventModal | 4 |
| 16 | Migrate DayPickerModal + QuickJumpModal | 5 |
| 17 | Migrate Index.razor | 3 |
| 18 | Add Display settings section | 5 |
| 19 | Delete Bootstrap files | 4 |
| 20 | Full build + test verification | 4 |
| 21 | Update documentation | 4 |
| **Total** | | **89 steps** |

### Task Dependencies

Tasks 1-3 (backend) can run in parallel with Tasks 4-5 (font + JS).
Task 6 depends on Tasks 2-3 (needs DTO + API).
Task 7 (CSS rewrite) can start alongside Tasks 1-6 but must complete before Tasks 8-18.
Tasks 8-18 (component migrations) are mostly independent of each other but all depend on Task 7.
Task 19 (delete Bootstrap) must come after all component migrations (Tasks 8-18).
Task 20 (verification) comes after everything.
Task 21 (docs) comes last.
