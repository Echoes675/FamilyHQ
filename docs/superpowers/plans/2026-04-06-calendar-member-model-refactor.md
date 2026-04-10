# Calendar Member Model Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the attendee-based multi-calendar model with a single-calendar + description-tag member model, delivering all 10 work items from the architecture document.

**Architecture:** Each event lives on exactly one Google calendar (individual or shared). Multi-member events are stored on a shared calendar, with assigned members identified by a `[members: ...]` tag in the event description. A content-hash extended property prevents infinite webhook echo loops. The kiosk projects events onto individual member columns at display time via an `EventMembers` DB junction table.

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core 10, PostgreSQL, Google Calendar API (REST via HttpClient), Reqnroll/Playwright E2E.

---

## Scope Note

This plan is large. The tasks fall into three natural groups that build on each other:
- **Group A (Tasks 1–9):** Backend core — schema, new services, refactored existing services
- **Group B (Tasks 10–13):** API layer and Simulator
- **Group C (Tasks 14–18):** UI and E2E

Tasks within each group must run sequentially. Groups must run in order.

---

## File Map

### Created
- `src/FamilyHQ.Core/Interfaces/IMemberTagParser.cs`
- `src/FamilyHQ.Core/Interfaces/ICalendarMigrationService.cs`
- `src/FamilyHQ.Services/Calendar/MemberTagParser.cs`
- `src/FamilyHQ.Services/Calendar/EventContentHash.cs`
- `src/FamilyHQ.Services/Calendar/CalendarMigrationService.cs`
- `src/FamilyHQ.WebUi/Components/Settings/SettingsCalendarsTab.razor`
- `tests/FamilyHQ.Services.Tests/Calendar/MemberTagParserTests.cs`
- `tests/FamilyHQ.Services.Tests/Calendar/EventContentHashTests.cs`
- `tests/FamilyHQ.Services.Tests/Calendar/CalendarMigrationServiceTests.cs`
- `tests-e2e/FamilyHQ.E2E.Features/CalendarSettings.feature`

### Modified
- `src/FamilyHQ.Core/Models/CalendarEvent.cs`
- `src/FamilyHQ.Core/Models/CalendarInfo.cs`
- `src/FamilyHQ.Core/Models/GoogleEventDetail.cs`
- `src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs`
- `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs`
- `src/FamilyHQ.Core/Interfaces/ICalendarRepository.cs`
- `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs`
- `src/FamilyHQ.Core/DTOs/CalendarEventDto.cs`
- `src/FamilyHQ.Core/DTOs/EventCalendarDto.cs`
- `src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs`
- `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs`
- `src/FamilyHQ.Data/Configurations/CalendarInfoConfiguration.cs`
- `src/FamilyHQ.Data/Repositories/CalendarRepository.cs`
- `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs`
- `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs`
- `src/FamilyHQ.Services/Calendar/CalendarEventService.cs`
- `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs`
- `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`
- `src/FamilyHQ.WebApi/Controllers/EventsController.cs`
- `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs`
- `src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor`
- `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor`
- `src/FamilyHQ.WebUi/Components/CalendarChipSelector.razor`
- `src/FamilyHQ.WebUi/Pages/Settings.razor`
- `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs`
- `src/FamilyHQ.WebUi/Services/CalendarApiService.cs`
- `tools/FamilyHQ.Simulator/Models/SimulatedEvent.cs`
- `tools/FamilyHQ.Simulator/Models/CalendarModel.cs`
- `tools/FamilyHQ.Simulator/Controllers/EventsController.cs`
- `tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs`
- `tools/FamilyHQ.Simulator/DTOs/GoogleEventRequest.cs`
- `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs`
- `tests/FamilyHQ.Services.Tests/Calendar/CalendarSyncServiceTests.cs`
- `tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientTests.cs`
- `tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs`
- `tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json`
- `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`
- `tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs`
- `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs`
- `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature`
- `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature`

---

## GROUP A — Backend Core

---

### Task 1: Schema — Replace CalendarEventCalendar with EventMembers; add IsShared + DisplayOrder

**Files:**
- Modify: `src/FamilyHQ.Core/Models/CalendarEvent.cs`
- Modify: `src/FamilyHQ.Core/Models/CalendarInfo.cs`
- Modify: `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs`
- Modify: `src/FamilyHQ.Data/Configurations/CalendarInfoConfiguration.cs`

> **Note:** After this task the build will fail until Task 8 (repo + service layer catches up). Commit each sub-step; do not run full tests until Task 8 complete.

- [ ] **Step 1.1: Update CalendarEvent model**

Replace `src/FamilyHQ.Core/Models/CalendarEvent.cs` with:

```csharp
namespace FamilyHQ.Core.Models;

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleEventId { get; set; } = null!;

    public string Title { get; set; } = null!;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }

    public string? Location { get; set; }
    public string? Description { get; set; }

    // FK to the CalendarInfo that owns this event in Google (individual or shared calendar).
    public Guid OwnerCalendarInfoId { get; set; }

    // Family members assigned to this event (for display projection).
    // For a 1-member event: contains that member's CalendarInfo.
    // For a shared event: contains all assigned members' CalendarInfo rows.
    public ICollection<CalendarInfo> Members { get; set; } = new List<CalendarInfo>();
}
```

- [ ] **Step 1.2: Update CalendarInfo model**

Replace `src/FamilyHQ.Core/Models/CalendarInfo.cs` with:

```csharp
namespace FamilyHQ.Core.Models;

public class CalendarInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;
    public string GoogleCalendarId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Color { get; set; }
    public bool IsVisible { get; set; } = true;

    // Marks this calendar as the shared calendar used for multi-member events.
    public bool IsShared { get; set; } = false;

    // Order of this calendar's column in the Agenda view (0 = leftmost).
    public int DisplayOrder { get; set; } = 0;

    // Navigation properties
    public SyncState? SyncState { get; set; }
}
```

- [ ] **Step 1.3: Update CalendarEventConfiguration**

Replace `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs` with:

```csharp
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("Events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.GoogleEventId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Location)
            .HasMaxLength(1000);

        builder.Property(e => e.Start)
            .HasConversion(v => v.ToUniversalTime(), v => v);

        builder.Property(e => e.End)
            .HasConversion(v => v.ToUniversalTime(), v => v);

        builder.HasIndex(e => e.GoogleEventId).IsUnique();
        builder.HasIndex(e => e.Start);
        builder.HasIndex(e => e.End);

        builder.Property(e => e.OwnerCalendarInfoId).IsRequired();

        builder.HasOne<CalendarInfo>()
            .WithMany()
            .HasForeignKey(e => e.OwnerCalendarInfoId)
            .OnDelete(DeleteBehavior.Restrict);

        // EventMembers junction: which family members are assigned to this event.
        builder.HasMany(e => e.Members)
            .WithMany()
            .UsingEntity(j => j.ToTable("EventMembers"));
    }
}
```

- [ ] **Step 1.4: Update CalendarInfoConfiguration**

Replace `src/FamilyHQ.Data/Configurations/CalendarInfoConfiguration.cs` with:

```csharp
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyHQ.Data.Configurations;

public class CalendarInfoConfiguration : IEntityTypeConfiguration<CalendarInfo>
{
    public void Configure(EntityTypeBuilder<CalendarInfo> builder)
    {
        builder.ToTable("Calendars");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.GoogleCalendarId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.DisplayName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Color)
            .HasMaxLength(50);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.IsShared)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasIndex(c => new { c.GoogleCalendarId, c.UserId }).IsUnique();

        builder.HasOne(c => c.SyncState)
            .WithOne(s => s.CalendarInfo)
            .HasForeignKey<SyncState>(s => s.CalendarInfoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 1.5: Create the EF Core migration**

```bash
cd src/FamilyHQ.Data.PostgreSQL
dotnet ef migrations add ReplaceCalendarEventCalendarWithEventMembers --project . --startup-project ../../src/FamilyHQ.WebApi
```

Expected: migration file created with `DropTable("CalendarEventCalendar")`, `CreateTable("EventMembers")`, `AddColumn IsShared`, `AddColumn DisplayOrder`.

Open the generated migration and verify the Up() method:
- Drops `CalendarEventCalendar`
- Creates `EventMembers (EventsId uuid NOT NULL, MembersId uuid NOT NULL, PRIMARY KEY (EventsId, MembersId))`
- Adds `IsShared boolean NOT NULL DEFAULT false` to `Calendars`
- Adds `DisplayOrder integer NOT NULL DEFAULT 0` to `Calendars`

- [ ] **Step 1.6: Commit**

```bash
git add src/FamilyHQ.Core/Models/CalendarEvent.cs
git add src/FamilyHQ.Core/Models/CalendarInfo.cs
git add src/FamilyHQ.Data/Configurations/
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git commit -m "feat(schema): replace CalendarEventCalendar with EventMembers; add IsShared and DisplayOrder to CalendarInfo"
```

---

### Task 2: Update Core DTOs and interfaces (IGoogleCalendarClient, ICalendarEventService, ICalendarRepository)

> Build will remain broken until Task 8 (implementations catch up).

**Files:**
- Modify: `src/FamilyHQ.Core/Models/GoogleEventDetail.cs`
- Modify: `src/FamilyHQ.Core/DTOs/EventCalendarDto.cs`
- Modify: `src/FamilyHQ.Core/DTOs/CalendarEventDto.cs`
- Modify: `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/ICalendarRepository.cs`

- [ ] **Step 2.1: Update GoogleEventDetail — remove AttendeeEmails, add ContentHash**

```csharp
// src/FamilyHQ.Core/Models/GoogleEventDetail.cs
namespace FamilyHQ.Core.Models;

/// <summary>
/// Lightweight result of IGoogleCalendarClient.GetEventAsync.
/// Used by the webhook handler to detect self-generated echo events.
/// </summary>
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    string? ContentHash);
```

- [ ] **Step 2.2: Update EventCalendarDto — add IsShared**

```csharp
// src/FamilyHQ.Core/DTOs/EventCalendarDto.cs
namespace FamilyHQ.Core.DTOs;

public record EventCalendarDto(Guid Id, string DisplayName, string? Color, bool IsShared = false);
```

- [ ] **Step 2.3: Update CalendarEventDto — rename Calendars to Members**

```csharp
// src/FamilyHQ.Core/DTOs/CalendarEventDto.cs
namespace FamilyHQ.Core.DTOs;

public record CalendarEventDto(
    Guid Id,
    string GoogleEventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<EventCalendarDto> Members);
```

- [ ] **Step 2.4: Update CreateEventRequest — CalendarInfoIds → MemberCalendarInfoIds**

```csharp
// src/FamilyHQ.Core/DTOs/CreateEventRequest.cs
namespace FamilyHQ.Core.DTOs;

public record CreateEventRequest(
    IReadOnlyList<Guid> MemberCalendarInfoIds, // min 1, no duplicates; determines shared vs individual calendar
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description); // user-visible description; [members:...] tag is managed automatically
```

- [ ] **Step 2.5: Update IGoogleCalendarClient — remove PatchEventAttendeesAsync**

```csharp
// src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IGoogleCalendarClient
{
    Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns events from the given calendar. Extended properties (content-hash) are included.
    /// </summary>
    Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(
        string googleCalendarId,
        DateTimeOffset? syncWindowStart,
        DateTimeOffset? syncWindowEnd,
        string? syncToken = null,
        CancellationToken ct = default);

    Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);
    Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, string contentHash, CancellationToken ct = default);
    Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);
    Task<string> MoveEventAsync(string sourceCalendarId, string googleEventId, string destinationCalendarId, CancellationToken ct = default);

    /// <summary>Returns null if the event is not found (404). Includes the content-hash extended property.</summary>
    Task<GoogleEventDetail?> GetEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default);
}
```

- [ ] **Step 2.6: Update ICalendarEventService — remove Add/RemoveCalendar, add SetMembersAsync**

```csharp
// src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarEventService
{
    /// <summary>
    /// Creates an event. Determines correct calendar (individual or shared) from MemberCalendarInfoIds.
    /// Writes [members:...] tag to description and content-hash extended property.
    /// </summary>
    Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates event fields. Does not change members — use SetMembersAsync for that.
    /// </summary>
    Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Replaces the full member list for an event. Rewrites the [members:...] tag, updates EventMembers,
    /// and migrates the event to the correct calendar if membership count crosses the 1/shared threshold.
    /// </summary>
    Task<CalendarEvent> SetMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default);

    /// <summary>Deletes the event from Google Calendar and the local DB.</summary>
    Task DeleteAsync(Guid eventId, CancellationToken ct = default);
}
```

- [ ] **Step 2.7: Update ICalendarRepository — EventMembers-aware queries**

```csharp
// src/FamilyHQ.Core/Interfaces/ICalendarRepository.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarRepository
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default);
    Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default);
    Task<CalendarInfo?> GetSharedCalendarAsync(CancellationToken ct = default);

    // Returns events owned by calendarInfoId (used by sync service per-calendar).
    Task<IReadOnlyList<CalendarEvent>> GetEventsByOwnerCalendarAsync(Guid calendarInfoId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    // Returns all events for the current user (by owner calendar), including Members nav.
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

    Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default);
    Task<CalendarEvent?> GetEventByGoogleEventIdAsync(string googleEventId, CancellationToken ct = default);
    Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default);

    Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default);
    Task RemoveCalendarAsync(Guid calendarInfoId, CancellationToken ct = default);
    Task UpdateCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default);

    Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default);
    Task DeleteEventAsync(Guid id, CancellationToken ct = default);

    Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default);
    Task AddSyncStateAsync(SyncState syncState, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2.8: Commit**

```bash
git add src/FamilyHQ.Core/
git commit -m "refactor(core): update DTOs and interfaces for member-tag model; remove attendee model"
```

---

### Task 3: IMemberTagParser — interface, implementation, and tests (TDD)

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IMemberTagParser.cs`
- Create: `src/FamilyHQ.Services/Calendar/MemberTagParser.cs`
- Create: `tests/FamilyHQ.Services.Tests/Calendar/MemberTagParserTests.cs`

- [ ] **Step 3.1: Define the interface**

```csharp
// src/FamilyHQ.Core/Interfaces/IMemberTagParser.cs
namespace FamilyHQ.Core.Interfaces;

public interface IMemberTagParser
{
    /// <summary>
    /// Extracts assigned member names from an event description.
    /// Checks for structured [members: Name1, Name2] tag first.
    /// Falls back to whole-word name matching against knownMemberNames.
    /// </summary>
    IReadOnlyList<string> ParseMembers(string? description, IReadOnlyList<string> knownMemberNames);

    /// <summary>
    /// Returns the description with the [members:...] tag removed (user-visible text only).
    /// </summary>
    string StripMemberTag(string? description);

    /// <summary>
    /// Replaces or inserts [members: Name1, Name2] in the description.
    /// Preserves any user-visible text outside the tag.
    /// </summary>
    string NormaliseDescription(string? description, IReadOnlyList<string> memberNames);
}
```

- [ ] **Step 3.2: Write failing tests**

```csharp
// tests/FamilyHQ.Services.Tests/Calendar/MemberTagParserTests.cs
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class MemberTagParserTests
{
    private static readonly string[] KnownNames = ["Eoin", "Sarah", "Kids"];
    private readonly MemberTagParser _sut = new();

    // ── ParseMembers ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseMembers_StructuredTag_ReturnsTaggedMembers()
    {
        var result = _sut.ParseMembers("Dentist appointment [members: Eoin, Sarah]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah"]);
    }

    [Fact]
    public void ParseMembers_StructuredTagOnly_ReturnsTaggedMembers()
    {
        var result = _sut.ParseMembers("[members: Kids]", KnownNames);
        result.Should().BeEquivalentTo(["Kids"]);
    }

    [Fact]
    public void ParseMembers_NoTag_FallsBackToNameMatching()
    {
        var result = _sut.ParseMembers("Eoin and Sarah collecting the kids from football", KnownNames);
        result.Should().Contain("Eoin").And.Contain("Sarah");
    }

    [Fact]
    public void ParseMembers_NoTagNoNameMatch_ReturnsEmpty()
    {
        var result = _sut.ParseMembers("Grocery shopping", KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseMembers_NullDescription_ReturnsEmpty()
    {
        var result = _sut.ParseMembers(null, KnownNames);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseMembers_NameMatchIsWholeWordOnly()
    {
        // "Eoins" should not match "Eoin"
        var result = _sut.ParseMembers("Eoins car service", KnownNames);
        result.Should().NotContain("Eoin");
    }

    [Fact]
    public void ParseMembers_StructuredTagIgnoresCase()
    {
        var result = _sut.ParseMembers("[members: eoin, SARAH]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin", "Sarah"]);
    }

    [Fact]
    public void ParseMembers_StructuredTagWithUnknownName_IgnoresUnknown()
    {
        var result = _sut.ParseMembers("[members: Eoin, Unknown]", KnownNames);
        result.Should().BeEquivalentTo(["Eoin"]);
    }

    // ── StripMemberTag ────────────────────────────────────────────────────────

    [Fact]
    public void StripMemberTag_RemovesTagLeavesRest()
    {
        var result = _sut.StripMemberTag("Dentist [members: Eoin, Sarah]");
        result.Trim().Should().Be("Dentist");
    }

    [Fact]
    public void StripMemberTag_NoTag_ReturnsOriginal()
    {
        var result = _sut.StripMemberTag("Dentist appointment");
        result.Should().Be("Dentist appointment");
    }

    [Fact]
    public void StripMemberTag_NullReturnsEmpty()
    {
        var result = _sut.StripMemberTag(null);
        result.Should().BeEmpty();
    }

    // ── NormaliseDescription ──────────────────────────────────────────────────

    [Fact]
    public void NormaliseDescription_InsertsTagWhenAbsent()
    {
        var result = _sut.NormaliseDescription("Dentist", ["Eoin"]);
        result.Should().Be("Dentist\n[members: Eoin]");
    }

    [Fact]
    public void NormaliseDescription_ReplacesExistingTag()
    {
        var result = _sut.NormaliseDescription("Dentist [members: Eoin]", ["Eoin", "Sarah"]);
        result.Should().Be("Dentist\n[members: Eoin, Sarah]");
    }

    [Fact]
    public void NormaliseDescription_EmptyMemberList_ProducesTagWithNoNames()
    {
        var result = _sut.NormaliseDescription(null, []);
        result.Should().Be("[members: ]");
    }
}
```

- [ ] **Step 3.3: Run the tests — confirm they fail**

```bash
cd tests/FamilyHQ.Services.Tests
dotnet test --filter "FullyQualifiedName~MemberTagParserTests" 2>&1 | head -20
```

Expected: compile error (MemberTagParser does not exist yet).

- [ ] **Step 3.4: Implement MemberTagParser**

```csharp
// src/FamilyHQ.Services/Calendar/MemberTagParser.cs
using System.Text.RegularExpressions;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Calendar;

public class MemberTagParser : IMemberTagParser
{
    // Matches [members: Name1, Name2] anywhere in the string (case-insensitive).
    private static readonly Regex TagRegex = new(
        @"\[members:\s*([^\]]*)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> ParseMembers(string? description, IReadOnlyList<string> knownMemberNames)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var tagMatch = TagRegex.Match(description);
        if (tagMatch.Success)
        {
            var tagContent = tagMatch.Groups[1].Value;
            return tagContent
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => knownMemberNames.FirstOrDefault(
                    k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();
        }

        // Fallback: whole-word name matching
        return knownMemberNames
            .Where(name => Regex.IsMatch(description, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
            .ToList();
    }

    public string StripMemberTag(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return string.Empty;

        var stripped = TagRegex.Replace(description, string.Empty);
        return stripped.Trim();
    }

    public string NormaliseDescription(string? description, IReadOnlyList<string> memberNames)
    {
        var tag = $"[members: {string.Join(", ", memberNames)}]";
        var stripped = StripMemberTag(description);

        return string.IsNullOrWhiteSpace(stripped)
            ? tag
            : $"{stripped}\n{tag}";
    }
}
```

- [ ] **Step 3.5: Run the tests — confirm they pass**

```bash
cd tests/FamilyHQ.Services.Tests
dotnet test --filter "FullyQualifiedName~MemberTagParserTests" -v n
```

Expected: all tests PASS.

- [ ] **Step 3.6: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IMemberTagParser.cs
git add src/FamilyHQ.Services/Calendar/MemberTagParser.cs
git add tests/FamilyHQ.Services.Tests/Calendar/MemberTagParserTests.cs
git commit -m "feat(services): add MemberTagParser for description-based member extraction"
```

---

### Task 4: EventContentHash — utility and tests (TDD)

**Files:**
- Create: `src/FamilyHQ.Services/Calendar/EventContentHash.cs`
- Create: `tests/FamilyHQ.Services.Tests/Calendar/EventContentHashTests.cs`

- [ ] **Step 4.1: Write failing tests**

```csharp
// tests/FamilyHQ.Services.Tests/Calendar/EventContentHashTests.cs
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class EventContentHashTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End   = new(2026, 4, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_SameInputs_ReturnsSameHash()
    {
        var h1 = EventContentHash.Compute("Title", Start, End, false, "desc");
        var h2 = EventContentHash.Compute("Title", Start, End, false, "desc");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_DifferentTitle_ReturnsDifferentHash()
    {
        var h1 = EventContentHash.Compute("Title A", Start, End, false, null);
        var h2 = EventContentHash.Compute("Title B", Start, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_DifferentStartTime_ReturnsDifferentHash()
    {
        var later = Start.AddHours(1);
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", later, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_NullVsEmptyDescription_AreEquivalent()
    {
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", Start, End, false, "");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_ReturnsNonEmptyLowercaseHexString()
    {
        var hash = EventContentHash.Compute("T", Start, End, false, null);
        hash.Should().NotBeNullOrEmpty();
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void Compute_StartInDifferentTimezone_SameUtcMoment_ReturnsSameHash()
    {
        var startUtc   = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
        var startLocal = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.FromHours(1)); // same UTC moment
        var h1 = EventContentHash.Compute("T", startUtc,   End, false, null);
        var h2 = EventContentHash.Compute("T", startLocal, End, false, null);
        h1.Should().Be(h2);
    }
}
```

- [ ] **Step 4.2: Run failing tests**

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~EventContentHashTests" 2>&1 | head -10
```

Expected: compile error.

- [ ] **Step 4.3: Implement EventContentHash**

```csharp
// src/FamilyHQ.Services/Calendar/EventContentHash.cs
using System.Security.Cryptography;
using System.Text;

namespace FamilyHQ.Services.Calendar;

public static class EventContentHash
{
    /// <summary>
    /// Computes a stable hex hash of the key event fields.
    /// Used to detect self-generated echo webhooks.
    /// </summary>
    public static string Compute(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        bool isAllDay,
        string? description)
    {
        var desc = string.IsNullOrEmpty(description) ? "" : description;
        var input = $"{title}|{start.ToUniversalTime():O}|{end.ToUniversalTime():O}|{isAllDay}|{desc}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 4.4: Run passing tests**

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~EventContentHashTests" -v n
```

Expected: all pass.

- [ ] **Step 4.5: Commit**

```bash
git add src/FamilyHQ.Services/Calendar/EventContentHash.cs
git add tests/FamilyHQ.Services.Tests/Calendar/EventContentHashTests.cs
git commit -m "feat(services): add EventContentHash utility for webhook echo detection"
```

---

### Task 5: ICalendarMigrationService — interface, implementation, tests (TDD)

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/ICalendarMigrationService.cs`
- Create: `src/FamilyHQ.Services/Calendar/CalendarMigrationService.cs`
- Create: `tests/FamilyHQ.Services.Tests/Calendar/CalendarMigrationServiceTests.cs`

- [ ] **Step 5.1: Define the interface**

```csharp
// src/FamilyHQ.Core/Interfaces/ICalendarMigrationService.cs
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarMigrationService
{
    /// <summary>
    /// Ensures the event lives on the correct calendar given its assigned members.
    /// Rule: 1 member → individual calendar; 2+ members → shared calendar.
    /// Migrates (create on target + delete on source) if the event is in the wrong place.
    /// Returns true if migration was performed.
    /// </summary>
    Task<bool> EnsureCorrectCalendarAsync(
        CalendarEvent calendarEvent,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default);
}
```

- [ ] **Step 5.2: Write failing tests**

```csharp
// tests/FamilyHQ.Services.Tests/Calendar/CalendarMigrationServiceTests.cs
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarMigrationServiceTests
{
    private static readonly Guid IndividualCalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SharedCalId     = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberBCalId    = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid EventId         = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static CalendarInfo IndividualCal => new()
        { Id = IndividualCalId, GoogleCalendarId = "eoin@", DisplayName = "Eoin", IsShared = false };
    private static CalendarInfo SharedCal => new()
        { Id = SharedCalId, GoogleCalendarId = "shared@", DisplayName = "Shared", IsShared = true };
    private static CalendarInfo MemberBCal => new()
        { Id = MemberBCalId, GoogleCalendarId = "sarah@", DisplayName = "Sarah", IsShared = false };

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo, CalendarMigrationService sut) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var logger = new Mock<ILogger<CalendarMigrationService>>();
        var sut    = new CalendarMigrationService(google.Object, repo.Object, logger.Object);
        return (google, repo, sut);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_SingleMemberOnCorrectCalendar_NoMigration()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = IndividualCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, default)).ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal]);

        migrated.Should().BeFalse();
        google.Verify(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_TwoMembersOnIndividualCalendar_MigratesToShared()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            Description = "note [members: Eoin, Sarah]",
            OwnerCalendarInfoId = IndividualCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, default)).ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                { e.GoogleEventId = "gid2"; return e; });
        google.Setup(g => g.DeleteEventAsync("eoin@", "gid1", default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(0);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        migrated.Should().BeTrue();
        google.Verify(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Once);
        google.Verify(g => g.DeleteEventAsync("eoin@", "gid1", default), Times.Once);
        evt.GoogleEventId.Should().Be("gid2");
        evt.OwnerCalendarInfoId.Should().Be(SharedCalId);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_OneMemberOnSharedCalendar_MigratesToIndividual()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            Description = "[members: Eoin]",
            OwnerCalendarInfoId = SharedCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(SharedCalId, default)).ReturnsAsync(SharedCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("eoin@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                { e.GoogleEventId = "gid3"; return e; });
        google.Setup(g => g.DeleteEventAsync("shared@", "gid1", default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(0);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal]);

        migrated.Should().BeTrue();
        google.Verify(g => g.CreateEventAsync("eoin@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Once);
        google.Verify(g => g.DeleteEventAsync("shared@", "gid1", default), Times.Once);
        evt.OwnerCalendarInfoId.Should().Be(IndividualCalId);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_MultiMemberOnSharedCalendar_NoMigration()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = SharedCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(SharedCalId, default)).ReturnsAsync(SharedCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        migrated.Should().BeFalse();
        google.Verify(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Never);
    }
}
```

- [ ] **Step 5.3: Run failing tests**

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~CalendarMigrationServiceTests" 2>&1 | head -15
```

Expected: compile error.

- [ ] **Step 5.4: Implement CalendarMigrationService**

```csharp
// src/FamilyHQ.Services/Calendar/CalendarMigrationService.cs
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarMigrationService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ILogger<CalendarMigrationService> logger) : ICalendarMigrationService
{
    public async Task<bool> EnsureCorrectCalendarAsync(
        CalendarEvent calendarEvent,
        IReadOnlyList<CalendarInfo> assignedMembers,
        CancellationToken ct = default)
    {
        var currentOwner = await calendarRepository.GetCalendarByIdAsync(calendarEvent.OwnerCalendarInfoId, ct)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found.");

        var sharedCal = await calendarRepository.GetSharedCalendarAsync(ct);

        bool shouldBeShared = assignedMembers.Count > 1;
        bool isCurrentlyShared = currentOwner.IsShared;

        if (shouldBeShared == isCurrentlyShared)
            return false; // already in the right place

        CalendarInfo targetCalendar;
        if (shouldBeShared)
        {
            if (sharedCal is null)
                throw new InvalidOperationException("No shared calendar is configured. Set IsShared = true on one calendar.");
            targetCalendar = sharedCal;
        }
        else
        {
            // Move to the sole assigned member's individual calendar
            targetCalendar = assignedMembers.Single(m => !m.IsShared);
        }

        logger.LogInformation(
            "Migrating event {GoogleEventId} from {Source} to {Target}.",
            calendarEvent.GoogleEventId, currentOwner.GoogleCalendarId, targetCalendar.GoogleCalendarId);

        // Create a copy on the target calendar
        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        var created = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        // Delete from the old calendar
        await googleCalendarClient.DeleteEventAsync(
            currentOwner.GoogleCalendarId, calendarEvent.GoogleEventId, ct);

        // Update the event record
        calendarEvent.GoogleEventId = created.GoogleEventId;
        calendarEvent.OwnerCalendarInfoId = targetCalendar.Id;

        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        return true;
    }
}
```

- [ ] **Step 5.5: Run passing tests**

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~CalendarMigrationServiceTests" -v n
```

Expected: all pass.

- [ ] **Step 5.6: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/ICalendarMigrationService.cs
git add src/FamilyHQ.Services/Calendar/CalendarMigrationService.cs
git add tests/FamilyHQ.Services.Tests/Calendar/CalendarMigrationServiceTests.cs
git commit -m "feat(services): add CalendarMigrationService to enforce individual/shared calendar invariant"
```

---

### Task 6: Update GoogleApiTypes and GoogleCalendarClient (extended properties, remove attendees)

**Files:**
- Modify: `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs`
- Modify: `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs`

- [ ] **Step 6.1: Update GoogleApiTypes**

Replace `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs` with:

```csharp
using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar.GoogleApi;

internal record GoogleApiEventDateTime(
    [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTime,
    [property: JsonPropertyName("date")]     string?         Date,
    [property: JsonPropertyName("timeZone")] string?         TimeZone);

internal record GoogleApiOrganizer(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("self")]  bool    Self);

internal record GoogleApiPrivateExtendedProperties(
    [property: JsonPropertyName("content-hash")] string? ContentHash);

internal record GoogleApiExtendedProperties(
    [property: JsonPropertyName("private")] GoogleApiPrivateExtendedProperties? Private);

internal record GoogleApiEvent(
    [property: JsonPropertyName("id")]                   string                   Id,
    [property: JsonPropertyName("iCalUID")]              string?                  ICalUID,
    [property: JsonPropertyName("status")]               string?                  Status,
    [property: JsonPropertyName("summary")]              string?                  Summary,
    [property: JsonPropertyName("description")]          string?                  Description,
    [property: JsonPropertyName("location")]             string?                  Location,
    [property: JsonPropertyName("start")]                GoogleApiEventDateTime?  Start,
    [property: JsonPropertyName("end")]                  GoogleApiEventDateTime?  End,
    [property: JsonPropertyName("organizer")]            GoogleApiOrganizer?      Organizer,
    [property: JsonPropertyName("extendedProperties")]   GoogleApiExtendedProperties? ExtendedProperties);

internal record GoogleApiEventList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiEvent> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);

internal record GoogleApiCalendarListEntry(
    [property: JsonPropertyName("id")]              string  Id,
    [property: JsonPropertyName("summary")]         string? Summary,
    [property: JsonPropertyName("summaryOverride")] string? SummaryOverride,
    [property: JsonPropertyName("backgroundColor")] string? BackgroundColor,
    [property: JsonPropertyName("foregroundColor")] string? ForegroundColor,
    [property: JsonPropertyName("accessRole")]      string? AccessRole);

internal record GoogleApiCalendarList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiCalendarListEntry> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);
```

- [ ] **Step 6.2: Rewrite GoogleCalendarClient**

Replace `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs` with:

```csharp
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar.GoogleApi;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

public class GoogleCalendarClient : IGoogleCalendarClient
{
    private readonly HttpClient _httpClient;
    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleCalendarClient> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoogleCalendarClient(
        HttpClient httpClient,
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IAccessTokenProvider accessTokenProvider,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleCalendarClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _tokenStore = tokenStore;
        _accessTokenProvider = accessTokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    private async Task SetAuthorizationHeaderAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessTokenProvider.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessTokenProvider.AccessToken);
            return;
        }

        var refreshToken = await _tokenStore.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("No refresh token available. User must authenticate first.");

        var accessToken = await _authService.RefreshAccessTokenAsync(refreshToken, ct);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/users/me/calendarList";
        var response = await _httpClient.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiCalendarList>(cancellationToken: ct);
        return result?.Items.Select(item => new CalendarInfo
        {
            GoogleCalendarId = item.Id,
            DisplayName = item.SummaryOverride ?? item.Summary ?? string.Empty,
            Color = item.BackgroundColor
        }) ?? Array.Empty<CalendarInfo>();
    }

    public async Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(
        string googleCalendarId,
        DateTimeOffset? syncWindowStart,
        DateTimeOffset? syncWindowEnd,
        string? syncToken = null,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var events = new List<CalendarEvent>();
        string? nextSyncToken = null;
        string? pageToken = null;

        do
        {
            var query = new List<string> { "singleEvents=true" };

            if (!string.IsNullOrEmpty(syncToken))
            {
                query.Add($"syncToken={Uri.EscapeDataString(syncToken)}");
            }
            else
            {
                if (syncWindowStart.HasValue)
                    query.Add($"timeMin={Uri.EscapeDataString(syncWindowStart.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
                if (syncWindowEnd.HasValue)
                    query.Add($"timeMax={Uri.EscapeDataString(syncWindowEnd.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
            }

            if (!string.IsNullOrEmpty(pageToken))
                query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");

            var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events?{string.Join("&", query)}";
            var response = await _httpClient.GetAsync(endpoint, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
                throw new InvalidOperationException("Sync token is no longer valid. Full sync required.");

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GoogleApiEventList>(cancellationToken: ct);
            if (result != null)
            {
                foreach (var item in result.Items)
                {
                    if (item.Status == "cancelled")
                    {
                        events.Add(new CalendarEvent { GoogleEventId = item.Id, Title = "CANCELLED_TOMBSTONE" });
                        continue;
                    }

                    var startParam = item.Start?.DateTime
                        ?? (item.Start?.Date != null ? DateTimeOffset.Parse(item.Start.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);
                    var endParam = item.End?.DateTime
                        ?? (item.End?.Date != null ? DateTimeOffset.Parse(item.End.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);

                    if (startParam == null || endParam == null) continue;

                    events.Add(new CalendarEvent
                    {
                        GoogleEventId = item.Id,
                        Title = item.Summary ?? "Untitled Event",
                        Start = startParam.Value,
                        End = endParam.Value,
                        IsAllDay = item.Start?.Date != null,
                        Location = item.Location,
                        Description = item.Description
                        // ContentHash is NOT stored on CalendarEvent; retrieved on-demand from Google via GetEventAsync
                    });
                }

                pageToken = result.NextPageToken;
                nextSyncToken = result.NextSyncToken;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        return (events, nextSyncToken);
    }

    public async Task<CalendarEvent> CreateEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events";
        var body = MapToGoogleEvent(calendarEvent, contentHash);
        var response = await _httpClient.PostAsJsonAsync(endpoint, body, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        calendarEvent.GoogleEventId = result!.Id;
        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(calendarEvent.GoogleEventId)}";
        var body = MapToGoogleEvent(calendarEvent, contentHash);
        var response = await _httpClient.PutAsJsonAsync(endpoint, body, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return calendarEvent;
    }

    public async Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Delete event {GoogleEventId} returned 404 — treating as success.", googleEventId);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<string> MoveEventAsync(
        string sourceCalendarId,
        string googleEventId,
        string destinationCalendarId,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(sourceCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}/move?destination={Uri.EscapeDataString(destinationCalendarId)}";
        var response = await _httpClient.PostAsync(endpoint, null, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        return result!.Id;
    }

    public async Task<GoogleEventDetail?> GetEventAsync(
        string googleCalendarId,
        string googleEventId,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        var response = await _httpClient.GetAsync(endpoint, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var apiEvent = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        if (apiEvent is null) return null;

        var contentHash = apiEvent.ExtendedProperties?.Private?.ContentHash;
        return new GoogleEventDetail(apiEvent.Id, apiEvent.Organizer?.Email, contentHash);
    }

    private static object MapToGoogleEvent(CalendarEvent evt, string contentHash)
    {
        var extendedProperties = new
        {
            @private = new Dictionary<string, string> { ["content-hash"] = contentHash }
        };

        if (evt.IsAllDay)
        {
            return new
            {
                summary = evt.Title,
                description = evt.Description,
                location = evt.Location,
                start = new { date = evt.Start.ToString("yyyy-MM-dd") },
                end = new { date = evt.End.ToString("yyyy-MM-dd") },
                extendedProperties
            };
        }

        return new
        {
            summary = evt.Title,
            description = evt.Description,
            location = evt.Location,
            start = new { dateTime = evt.Start.ToString("yyyy-MM-ddTHH:mm:ssK") },
            end = new { dateTime = evt.End.ToString("yyyy-MM-ddTHH:mm:ssK") },
            extendedProperties
        };
    }
}
```

- [ ] **Step 6.3: Update GoogleCalendarClientTests to remove PatchEventAttendeesAsync references**

In `tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientTests.cs`, search for any test that references `PatchEventAttendeesAsync` and remove it. Update `CreateEventAsync` and `UpdateEventAsync` test call signatures to include the new `contentHash` string parameter (pass any non-empty string like `"testhash"`).

Run:
```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~GoogleCalendarClient" -v n
```

Expected: all pass.

- [ ] **Step 6.4: Commit**

```bash
git add src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs
git add src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs
git add tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientTests.cs
git commit -m "refactor(services): update GoogleCalendarClient to use extended properties for content-hash; remove attendee model"
```

---

### Task 7: Update CalendarRepository

**Files:**
- Modify: `src/FamilyHQ.Data/Repositories/CalendarRepository.cs`

- [ ] **Step 7.1: Rewrite CalendarRepository**

Replace `src/FamilyHQ.Data/Repositories/CalendarRepository.cs` with:

```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class CalendarRepository : ICalendarRepository
{
    private readonly FamilyHqDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CalendarRepository(FamilyHqDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    private string CurrentUserId => _currentUserService.UserId ?? string.Empty;

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return Array.Empty<CalendarInfo>();

        return await _context.Calendars
            .AsNoTracking()
            .Where(c => c.UserId == CurrentUserId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Calendars
            .Include(c => c.SyncState)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<CalendarInfo?> GetSharedCalendarAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return null;

        return await _context.Calendars
            .FirstOrDefaultAsync(c => c.UserId == CurrentUserId && c.IsShared, ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsByOwnerCalendarAsync(
        Guid calendarInfoId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Members)
            .Where(e => e.OwnerCalendarInfoId == calendarInfoId && e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return Array.Empty<CalendarEvent>();

        return await _context.Events
            .AsNoTracking()
            .Include(e => e.Members)
            .Where(e => e.Start < end && e.End > start
                     && _context.Calendars.Any(c => c.Id == e.OwnerCalendarInfoId && c.UserId == CurrentUserId))
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Members)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<CalendarEvent?> GetEventByGoogleEventIdAsync(string googleEventId, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Members)
            .FirstOrDefaultAsync(e => e.GoogleEventId == googleEventId, ct);
    }

    public async Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        return await _context.SyncStates
            .FirstOrDefaultAsync(s => s.CalendarInfoId == calendarInfoId, ct);
    }

    public async Task RemoveCalendarAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        var calendar = await _context.Calendars
            .Include(c => c.SyncState)
            .FirstOrDefaultAsync(c => c.Id == calendarInfoId, ct);

        if (calendar == null) return;

        // Delete all events owned by this calendar (members junction rows cascade via EF)
        var ownedEvents = await _context.Events
            .Where(e => e.OwnerCalendarInfoId == calendarInfoId)
            .ToListAsync(ct);

        _context.Events.RemoveRange(ownedEvents);

        if (calendar.SyncState != null)
            _context.SyncStates.Remove(calendar.SyncState);

        _context.Calendars.Remove(calendar);
    }

    public Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default)
    {
        calendarInfo.UserId = _currentUserService.UserId
            ?? throw new InvalidOperationException("Cannot add calendar: no authenticated user.");
        _context.Calendars.Add(calendarInfo);
        return Task.CompletedTask;
    }

    public Task UpdateCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default)
    {
        _context.Calendars.Update(calendarInfo);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Attach member CalendarInfos so EF registers EventMembers join rows correctly.
        var trackedMembers = calendarEvent.Members
            .Select(m => _context.Entry(m).State == EntityState.Detached
                ? _context.Calendars.Attach(m).Entity
                : m)
            .ToList();

        calendarEvent.Members = trackedMembers;
        _context.Events.Add(calendarEvent);
        return Task.CompletedTask;
    }

    public Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        _context.Events.Update(calendarEvent);
        return Task.CompletedTask;
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken ct = default)
    {
        var evt = await _context.Events.FindAsync([id], ct);
        if (evt != null)
            _context.Events.Remove(evt);
    }

    public Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        var entry = _context.Entry(syncState);
        if (entry.State == EntityState.Detached)
            _context.SyncStates.Update(syncState);
        return Task.CompletedTask;
    }

    public Task AddSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        _context.SyncStates.Add(syncState);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
```

- [ ] **Step 7.2: Commit**

```bash
git add src/FamilyHQ.Data/Repositories/CalendarRepository.cs
git commit -m "refactor(data): update CalendarRepository for EventMembers model; add GetSharedCalendarAsync"
```

---

### Task 8: Rewrite CalendarEventService and update CalendarSyncService

**Files:**
- Modify: `src/FamilyHQ.Services/Calendar/CalendarEventService.cs`
- Modify: `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs`
- Modify: `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`

- [ ] **Step 8.1: Rewrite CalendarEventService**

Replace `src/FamilyHQ.Services/Calendar/CalendarEventService.cs` with:

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ICalendarMigrationService migrationService,
    IMemberTagParser memberTagParser,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var assignedMembers = request.MemberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new InvalidOperationException($"CalendarInfoId {id} is not known to the user."))
            .ToList();

        // Determine target calendar
        var targetCalendar = assignedMembers.Count == 1
            ? assignedMembers[0]
            : await calendarRepository.GetSharedCalendarAsync(ct)
              ?? throw new InvalidOperationException("No shared calendar configured for multi-member events.");

        // Build description with member tag
        var memberNames = assignedMembers.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        var calendarEvent = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = fullDescription,
            OwnerCalendarInfoId = targetCalendar.Id,
            Members = assignedMembers
        };

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        calendarEvent = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        await calendarRepository.AddEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {GoogleEventId} created on calendar {CalendarId}.",
            calendarEvent.GoogleEventId, targetCalendar.GoogleCalendarId);

        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.First(c => c.Id == calendarEvent.OwnerCalendarInfoId);

        // Preserve existing member tag; only update user-visible description
        var memberNames = calendarEvent.Members.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        calendarEvent.Title = request.Title;
        calendarEvent.Start = request.Start;
        calendarEvent.End = request.End;
        calendarEvent.IsAllDay = request.IsAllDay;
        calendarEvent.Location = request.Location;
        calendarEvent.Description = fullDescription;

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);
        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} updated.", eventId);
        return calendarEvent;
    }

    public async Task<CalendarEvent> SetMembersAsync(
        Guid eventId,
        IReadOnlyList<Guid> memberCalendarInfoIds,
        CancellationToken ct = default)
    {
        if (memberCalendarInfoIds.Count == 0)
            throw new InvalidOperationException("At least one member is required.");

        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var newMembers = memberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new InvalidOperationException($"CalendarInfoId {id} is not known to the user."))
            .ToList();

        // Update description with new member tag
        var strippedDescription = memberTagParser.StripMemberTag(calendarEvent.Description);
        var memberNames = newMembers.Select(m => m.DisplayName).ToList();
        calendarEvent.Description = memberTagParser.NormaliseDescription(strippedDescription, memberNames);
        calendarEvent.Members = newMembers;

        // Migrate if the individual/shared invariant is violated.
        // EnsureCorrectCalendarAsync already writes to Google and saves the DB if it migrates.
        var migrated = await migrationService.EnsureCorrectCalendarAsync(calendarEvent, newMembers, ct);

        if (!migrated)
        {
            // No migration: write updated description/members to Google and DB.
            var ownerCalendar = allCalendars.First(c => c.Id == calendarEvent.OwnerCalendarInfoId);
            var hash = EventContentHash.Compute(
                calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
                calendarEvent.IsAllDay, calendarEvent.Description);

            await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);
            await calendarRepository.UpdateEventAsync(calendarEvent, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }

        logger.LogInformation("Members updated for event {EventId}.", eventId);
        return calendarEvent;
    }

    public async Task DeleteAsync(Guid eventId, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.First(c => c.Id == calendarEvent.OwnerCalendarInfoId);

        await googleCalendarClient.DeleteEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent.GoogleEventId, ct);
        await calendarRepository.DeleteEventAsync(eventId, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} deleted.", eventId);
    }
}
```

- [ ] **Step 8.2: Rewrite CalendarSyncService**

Replace `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs` with:

```csharp
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarSyncService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    IMemberTagParser memberTagParser,
    ILogger<CalendarSyncService> logger) : ICalendarSyncService
{
    public async Task SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        logger.LogInformation("Starting full sync from {Start} to {End}", startDate, endDate);

        var googleCalendars = (await googleCalendarClient.GetCalendarsAsync(ct)).ToList();
        var localCalendars  = await calendarRepository.GetCalendarsAsync(ct);

        // Remove obsolete local calendars
        var obsolete = localCalendars
            .Where(local => !googleCalendars.Any(g => g.GoogleCalendarId == local.GoogleCalendarId))
            .ToList();

        foreach (var cal in obsolete)
        {
            logger.LogInformation("Removing obsolete calendar {CalendarId}", cal.Id);
            await calendarRepository.RemoveCalendarAsync(cal.Id, ct);
        }

        if (obsolete.Count > 0)
            await calendarRepository.SaveChangesAsync(ct);

        foreach (var googleCal in googleCalendars)
        {
            var localCal = localCalendars.FirstOrDefault(c => c.GoogleCalendarId == googleCal.GoogleCalendarId);
            if (localCal == null)
            {
                await calendarRepository.AddCalendarAsync(googleCal, ct);
                await calendarRepository.SaveChangesAsync(ct);
                localCal = googleCal;
            }

            await SyncAsync(localCal.Id, startDate, endDate, ct);
        }

        logger.LogInformation("Finished syncing all calendars.");
    }

    public async Task SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var calendar = await calendarRepository.GetCalendarByIdAsync(calendarInfoId, ct);
        if (calendar == null)
        {
            logger.LogWarning("Calendar {CalendarId} not found. Skipping sync.", calendarInfoId);
            return;
        }

        bool isNewSyncState = false;
        var syncState = await calendarRepository.GetSyncStateAsync(calendarInfoId, ct);
        if (syncState == null)
        {
            syncState = new SyncState { CalendarInfoId = calendarInfoId };
            isNewSyncState = true;
        }

        bool isFullSync = string.IsNullOrEmpty(syncState.SyncToken);

        logger.LogInformation("Syncing {CalendarName}. FullSync={IsFullSync}", calendar.DisplayName, isFullSync);

        try
        {
            var (events, nextSyncToken) = await googleCalendarClient.GetEventsAsync(
                calendar.GoogleCalendarId,
                isFullSync ? startDate : null,
                isFullSync ? endDate : null,
                syncState.SyncToken,
                ct);

            var allLocalCalendars = await calendarRepository.GetCalendarsAsync(ct);
            var knownMemberNames  = allLocalCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
            var calendarByName    = allLocalCalendars.Where(c => !c.IsShared)
                .ToDictionary(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

            if (isFullSync)
            {
                // Tombstone events no longer present in Google
                var existingEvents   = await calendarRepository.GetEventsByOwnerCalendarAsync(calendarInfoId, startDate, endDate, ct);
                var fetchedGoogleIds = events.Select(e => e.GoogleEventId).ToHashSet();
                var obsoleteEvents   = existingEvents.Where(e => !fetchedGoogleIds.Contains(e.GoogleEventId));

                foreach (var obsolete in obsoleteEvents)
                    await calendarRepository.DeleteEventAsync(obsolete.Id, ct);
            }

            foreach (var evt in events)
            {
                if (evt.Title == "CANCELLED_TOMBSTONE")
                {
                    var tracked = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                    if (tracked != null)
                        await calendarRepository.DeleteEventAsync(tracked.Id, ct);
                    continue;
                }

                // Derive members from description
                var parsedNames   = memberTagParser.ParseMembers(evt.Description, knownMemberNames);
                var parsedMembers = parsedNames
                    .Where(n => calendarByName.ContainsKey(n))
                    .Select(n => calendarByName[n])
                    .ToList();

                // If no members parsed and this is an individual calendar, default to owning calendar's member
                if (parsedMembers.Count == 0 && !calendar.IsShared)
                    parsedMembers.Add(calendar);

                var existing = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                if (existing != null)
                {
                    existing.Title       = evt.Title;
                    existing.Start       = evt.Start;
                    existing.End         = evt.End;
                    existing.IsAllDay    = evt.IsAllDay;
                    existing.Location    = evt.Location;
                    existing.Description = evt.Description;
                    existing.Members     = parsedMembers;
                    await calendarRepository.UpdateEventAsync(existing, ct);
                }
                else
                {
                    evt.OwnerCalendarInfoId = calendar.Id;
                    evt.Members             = parsedMembers;
                    await calendarRepository.AddEventAsync(evt, ct);
                }
            }

            syncState.SyncToken    = nextSyncToken;
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd   = endDate;
            }

            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);

            await calendarRepository.SaveChangesAsync(ct);
            logger.LogInformation("Synced {Count} events for {CalendarName}.", events.Count(), calendar.DisplayName);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no longer valid"))
        {
            logger.LogWarning("Sync token expired for {CalendarName}. Restarting full sync.", calendar.DisplayName);
            syncState.SyncToken = null;
            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);
            await calendarRepository.SaveChangesAsync(ct);
            await SyncAsync(calendarInfoId, startDate, endDate, ct);
        }
    }
}
```

- [ ] **Step 8.3: Register new services in ServiceCollectionExtensions**

Add the following lines in `AddFamilyHqServices` after the existing calendar registrations:

```csharp
services.AddScoped<IMemberTagParser, MemberTagParser>();
services.AddScoped<ICalendarMigrationService, CalendarMigrationService>();
```

- [ ] **Step 8.4: Verify solution builds**

```bash
cd D:/Git/Echoes675/FamilyHQ
dotnet build FamilyHQ.sln
```

Expected: 0 errors. Fix any remaining compile errors before proceeding.

- [ ] **Step 8.5: Run affected unit tests**

```bash
dotnet test tests/FamilyHQ.Services.Tests -v n 2>&1 | tail -30
```

Expected: CalendarEventServiceTests and CalendarSyncServiceTests will have failures due to interface changes. Update them in Step 8.6.

- [ ] **Step 8.6: Update CalendarEventServiceTests**

Rewrite `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs` to reflect the new interface:

- Remove all tests for `AddCalendarAsync` and `RemoveCalendarAsync`
- Add `Mock<ICalendarMigrationService>` and `Mock<IMemberTagParser>` to the SUT factory
- Update `CreateAsync` tests: `CreateEventRequest` uses `MemberCalendarInfoIds` instead of `CalendarInfoIds`
- Add test `CreateAsync_SingleMember_UsesIndividualCalendar`
- Add test `CreateAsync_TwoMembers_UsesSharedCalendar`
- Add test `SetMembersAsync_OneToTwo_TriggersMigration`
- Replace mock for `CreateEventAsync`/`UpdateEventAsync` to include the `contentHash` string parameter

Key structure for the SUT factory:

```csharp
private static (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
    Mock<ICalendarMigrationService> migration, Mock<IMemberTagParser> tagParser,
    CalendarEventService sut) CreateSut()
{
    var google    = new Mock<IGoogleCalendarClient>();
    var repo      = new Mock<ICalendarRepository>();
    var migration = new Mock<ICalendarMigrationService>();
    var tagParser = new Mock<IMemberTagParser>();
    var logger    = new Mock<ILogger<CalendarEventService>>();

    // Default: tag parser returns normalised description unchanged
    tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
             .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
    tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
             .Returns((string d) => d ?? string.Empty);

    var sut = new CalendarEventService(google.Object, repo.Object, migration.Object, tagParser.Object, logger.Object);
    return (google, repo, migration, tagParser, sut);
}
```

- [ ] **Step 8.7: Update CalendarSyncServiceTests similarly**

The `CalendarSyncService` constructor now takes `IMemberTagParser`. Update tests:
- Add `Mock<IMemberTagParser>` to CreateSut
- Update calls from `GetEventsAsync(calendarInfoId, ...)` to `GetEventsByOwnerCalendarAsync`
- Verify that when a sync event has `[members: Eoin]` in description, the member is set

- [ ] **Step 8.8: Run all unit tests**

```bash
dotnet test tests/ -v n 2>&1 | tail -20
```

Expected: all pass.

- [ ] **Step 8.9: Commit**

```bash
git add src/FamilyHQ.Services/
git add tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs
git add tests/FamilyHQ.Services.Tests/Calendar/CalendarSyncServiceTests.cs
git commit -m "refactor(services): rewrite CalendarEventService and CalendarSyncService for member-tag model"
```

---

### Task 9: Update CreateEventRequest validator

**Files:**
- Modify: `src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs`
- Modify: `tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs`

- [ ] **Step 9.1: Update validator**

Open `src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs` and change any reference to `CalendarInfoIds` to `MemberCalendarInfoIds`. The validation rules (min 1, no duplicates) remain the same.

```csharp
RuleFor(x => x.MemberCalendarInfoIds)
    .NotEmpty().WithMessage("At least one member is required.")
    .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Duplicate member IDs are not allowed.");
```

- [ ] **Step 9.2: Update validator tests**

In `tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs`, replace `CalendarInfoIds` with `MemberCalendarInfoIds` in all test data. Run:

```bash
dotnet test tests/FamilyHQ.Core.Tests --filter "FullyQualifiedName~CreateEventRequestValidator" -v n
```

Expected: all pass.

- [ ] **Step 9.3: Commit**

```bash
git add src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs
git add tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs
git commit -m "refactor(core): update CreateEventRequest validator for MemberCalendarInfoIds"
```

---

## GROUP B — API Layer and Simulator

---

### Task 10: Update API controllers

**Files:**
- Modify: `src/FamilyHQ.WebApi/Controllers/EventsController.cs`
- Modify: `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs`

- [ ] **Step 10.1: Rewrite EventsController**

Replace `src/FamilyHQ.WebApi/Controllers/EventsController.cs` with:

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly ICalendarEventService _service;
    private readonly ILogger<EventsController> _logger;

    public EventsController(ICalendarEventService service, ILogger<EventsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var created = await _service.CreateAsync(request, ct);
        return Created($"/api/events/{created.Id}", MapToDto(created));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.UpdateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var updated = await _service.UpdateAsync(eventId, request, ct);
        return Ok(MapToDto(updated));
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid eventId, CancellationToken ct)
    {
        await _service.DeleteAsync(eventId, ct);
        return NoContent();
    }

    /// <summary>Replaces the full member list for an event.</summary>
    [HttpPut("{eventId:guid}/members")]
    public async Task<IActionResult> SetMembers(Guid eventId, [FromBody] SetEventMembersRequest request, CancellationToken ct)
    {
        if (request.MemberCalendarInfoIds == null || request.MemberCalendarInfoIds.Count == 0)
            return BadRequest("At least one member is required.");

        var updated = await _service.SetMembersAsync(eventId, request.MemberCalendarInfoIds, ct);
        return Ok(MapToDto(updated));
    }

    private static CalendarEventDto MapToDto(CalendarEvent e) => new(
        e.Id,
        e.GoogleEventId,
        e.Title,
        e.Start,
        e.End,
        e.IsAllDay,
        e.Location,
        e.Description,
        e.Members.Select(m => new EventCalendarDto(m.Id, m.DisplayName, m.Color, m.IsShared)).ToList());
}
```

Also add the new DTO to `src/FamilyHQ.Core/DTOs/`:

```csharp
// src/FamilyHQ.Core/DTOs/SetEventMembersRequest.cs
namespace FamilyHQ.Core.DTOs;

public record SetEventMembersRequest(IReadOnlyList<Guid> MemberCalendarInfoIds);
```

- [ ] **Step 10.2: Update CalendarsController**

Replace `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs` with:

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CalendarsController : ControllerBase
{
    private readonly ICalendarRepository _calendarRepository;
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(ICalendarRepository calendarRepository, ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var dtos = calendars
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color, c.IsShared));
        return Ok(dtos);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEventsForMonth([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return BadRequest("Invalid year or month.");

        var firstDay = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var startDay = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        var nextMonth = firstDay.AddMonths(1);
        var endDay = nextMonth.AddDays(7 - (int)Math.Max(1, (int)nextMonth.DayOfWeek));

        var events = await _calendarRepository.GetEventsAsync(start: startDay, end: endDay.AddDays(14), ct: ct);
        var allCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        // Expose only non-shared (visible) calendars in the month view response
        var visibleCalendars = allCalendars.Where(c => !c.IsShared && c.IsVisible).ToHashSet();

        var monthView = new MonthViewDto { Year = year, Month = month };

        foreach (var evt in events)
        {
            // Project event into each assigned visible member's lane
            var visibleMembers = evt.Members.Where(m => visibleCalendars.Contains(m)).ToList();
            if (visibleMembers.Count == 0) continue;

            foreach (var member in visibleMembers)
            {
                var dto = new CalendarEventDto(
                    evt.Id,
                    evt.GoogleEventId,
                    evt.Title,
                    evt.Start,
                    evt.End,
                    evt.IsAllDay,
                    evt.Location,
                    memberTagParser_StripTag(evt.Description), // user-visible description only
                    evt.Members.Select(m => new EventCalendarDto(m.Id, m.DisplayName, m.Color, m.IsShared)).ToList());

                var current = evt.Start.Date;
                var last    = evt.End.AddTicks(-1).Date;
                int daysProcessed = 0;
                while (current <= last && daysProcessed < 366)
                {
                    var dateKey = current.ToString("yyyy-MM-dd");
                    if (!monthView.Days.ContainsKey(dateKey))
                        monthView.Days[dateKey] = [];
                    monthView.Days[dateKey].Add(dto);
                    current = current.AddDays(1);
                    daysProcessed++;
                }
            }
        }

        return Ok(monthView);
    }

    // Strip [members:...] tag from the description returned to the UI
    private static string? memberTagParser_StripTag(string? description)
    {
        if (string.IsNullOrEmpty(description)) return null;
        var stripped = System.Text.RegularExpressions.Regex
            .Replace(description, @"\[members:\s*[^\]]*\]", string.Empty)
            .Trim();
        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }

    /// <summary>Updates visibility and shared designation for a calendar.</summary>
    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateCalendarSettings(
        Guid id, [FromBody] CalendarSettingsRequest request, CancellationToken ct)
    {
        var calendar = await _calendarRepository.GetCalendarByIdAsync(id, ct);
        if (calendar == null) return NotFound();

        // Only one calendar can be shared at a time
        if (request.IsShared)
        {
            var currentShared = await _calendarRepository.GetSharedCalendarAsync(ct);
            if (currentShared != null && currentShared.Id != id)
            {
                currentShared.IsShared = false;
                await _calendarRepository.UpdateCalendarAsync(currentShared, ct);
            }
        }

        calendar.IsVisible = request.IsVisible;
        calendar.IsShared  = request.IsShared;
        await _calendarRepository.UpdateCalendarAsync(calendar, ct);
        await _calendarRepository.SaveChangesAsync(ct);

        return Ok(new EventCalendarDto(calendar.Id, calendar.DisplayName, calendar.Color, calendar.IsShared));
    }

    /// <summary>Saves the display order for all calendars (agenda column order).</summary>
    [HttpPut("order")]
    public async Task<IActionResult> SaveOrder([FromBody] CalendarOrderRequest request, CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var lookup    = calendars.ToDictionary(c => c.Id);

        foreach (var (calendarId, order) in request.Order)
        {
            if (!lookup.TryGetValue(calendarId, out var cal)) continue;
            cal.DisplayOrder = order;
            await _calendarRepository.UpdateCalendarAsync(cal, ct);
        }

        await _calendarRepository.SaveChangesAsync(ct);
        return NoContent();
    }
}
```

Also add the request DTOs:

```csharp
// src/FamilyHQ.Core/DTOs/CalendarSettingsRequest.cs
namespace FamilyHQ.Core.DTOs;
public record CalendarSettingsRequest(bool IsVisible, bool IsShared);

// src/FamilyHQ.Core/DTOs/CalendarOrderRequest.cs
namespace FamilyHQ.Core.DTOs;
public record CalendarOrderRequest(Dictionary<Guid, int> Order);
```

> **Note:** The inline `memberTagParser_StripTag` in the controller is intentional — it avoids a service dependency in the controller. If the stripping logic grows complex, inject `IMemberTagParser` instead.

- [ ] **Step 10.3: Build and fix any remaining errors**

```bash
dotnet build src/FamilyHQ.WebApi
```

Expected: 0 errors.

- [ ] **Step 10.4: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/
git add src/FamilyHQ.Core/DTOs/SetEventMembersRequest.cs
git add src/FamilyHQ.Core/DTOs/CalendarSettingsRequest.cs
git add src/FamilyHQ.Core/DTOs/CalendarOrderRequest.cs
git commit -m "refactor(api): update EventsController and CalendarsController for member-tag model; add settings and order endpoints"
```

---

### Task 11: Update Simulator

**Files:**
- Modify: `tools/FamilyHQ.Simulator/Models/SimulatedEvent.cs`
- Modify: `tools/FamilyHQ.Simulator/Models/CalendarModel.cs`
- Modify: `tools/FamilyHQ.Simulator/Controllers/EventsController.cs`
- Modify: `tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs`
- Modify: `tools/FamilyHQ.Simulator/DTOs/GoogleEventRequest.cs`

- [ ] **Step 11.1: Add ContentHash to SimulatedEvent**

```csharp
// tools/FamilyHQ.Simulator/Models/SimulatedEvent.cs
namespace FamilyHQ.Simulator.Models;

public class SimulatedEvent
{
    public string Id { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? ContentHash { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? UserId { get; set; }
    public bool IsDeleted { get; set; }
}
```

- [ ] **Step 11.2: Add IsShared to CalendarModel**

```csharp
// tools/FamilyHQ.Simulator/Models/CalendarModel.cs
namespace FamilyHQ.Simulator.Models;

public class CalendarModel
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? BackgroundColor { get; set; }
    public bool IsShared { get; set; } = false;
}
```

- [ ] **Step 11.3: Create migration for ContentHash in Simulator DB**

```bash
cd tools/FamilyHQ.Simulator
dotnet ef migrations add AddContentHashToEvent
```

Verify migration adds `ContentHash text NULL` to `Events`.

- [ ] **Step 11.4: Add Description to BackdoorEventRequest**

```csharp
// tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs
namespace FamilyHQ.Simulator.DTOs;

public class BackdoorEventRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? CalendarId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
}
```

- [ ] **Step 11.5: Update Simulator EventsController to include extendedProperties in responses**

In `tools/FamilyHQ.Simulator/Controllers/EventsController.cs`, update `MapEventResponse` to include extended properties:

```csharp
private static object MapEventResponse(SimulatedEvent e, IReadOnlyList<string> attendeeCalendarIds) => new
{
    id          = e.Id,
    status      = e.IsDeleted ? "cancelled" : "confirmed",
    summary     = e.Summary,
    location    = e.Location,
    description = e.Description,
    start       = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
    end         = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd") }   : new { dateTime = e.EndTime.ToString("O") },
    organizer   = new { email = e.CalendarId, self = true },
    extendedProperties = e.ContentHash != null
        ? (object)new { @private = new Dictionary<string, string> { ["content-hash"] = e.ContentHash } }
        : null
};
```

Also update `CreateEvent` and `UpdateEvent` to persist `ContentHash` from the `GoogleEventRequest` body (which the WebApi will now include):

In `GoogleEventRequest.cs`, add `ExtendedProperties` field:

```csharp
// tools/FamilyHQ.Simulator/DTOs/GoogleEventRequest.cs
// Add to existing record:
[JsonPropertyName("extendedProperties")]
public GoogleEventExtendedPropertiesRequest? ExtendedProperties { get; set; }

public class GoogleEventExtendedPropertiesRequest
{
    [JsonPropertyName("private")]
    public Dictionary<string, string>? Private { get; set; }
}
```

In `CreateEvent` action:
```csharp
ContentHash = body.ExtendedProperties?.Private?.GetValueOrDefault("content-hash"),
```

In `UpdateEvent` action:
```csharp
if (body.ExtendedProperties?.Private?.TryGetValue("content-hash", out var hash) == true)
    existing.ContentHash = hash;
```

Also update `BackdoorEventsController.AddEvent` to persist `Description`:
```csharp
Description = body.Description,
```

Remove the `PatchEvent` action (attendee patching) from the Simulator `EventsController` — it's no longer called. Or leave it as a no-op to avoid breaking existing e2e tests that haven't been updated yet.

- [ ] **Step 11.6: Build Simulator**

```bash
dotnet build tools/FamilyHQ.Simulator
```

Expected: 0 errors.

- [ ] **Step 11.7: Commit**

```bash
git add tools/FamilyHQ.Simulator/
git commit -m "feat(simulator): add content-hash extended property support; add IsShared to CalendarModel; add Description to BackdoorEventRequest"
```

---

## GROUP C — UI and E2E

---

### Task 12: Calendar Settings tab

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsCalendarsTab.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor`
- Modify: `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/CalendarApiService.cs`

- [ ] **Step 12.1: Add calendar API calls to ICalendarApiService and CalendarApiService**

Add to `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs`:
```csharp
Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct = default);
Task UpdateCalendarSettingsAsync(Guid calendarId, bool isVisible, bool isShared, CancellationToken ct = default);
Task SaveCalendarOrderAsync(Dictionary<Guid, int> order, CancellationToken ct = default);
Task<CalendarEventViewModel> SetEventMembersAsync(Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default);
```

Implement in `CalendarApiService.cs`:

```csharp
public async Task UpdateCalendarSettingsAsync(Guid calendarId, bool isVisible, bool isShared, CancellationToken ct = default)
{
    var response = await httpClient.PutAsJsonAsync(
        $"api/calendars/{calendarId}/settings",
        new { isVisible, isShared }, ct);
    response.EnsureSuccessStatusCode();
}

public async Task SaveCalendarOrderAsync(Dictionary<Guid, int> order, CancellationToken ct = default)
{
    var response = await httpClient.PutAsJsonAsync("api/calendars/order", new { order }, ct);
    response.EnsureSuccessStatusCode();
}

public async Task<CalendarEventViewModel> SetEventMembersAsync(
    Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default)
{
    var response = await httpClient.PutAsJsonAsync(
        $"api/events/{eventId}/members",
        new { memberCalendarInfoIds }, ct);
    response.EnsureSuccessStatusCode();

    var dto = await response.Content.ReadFromJsonAsync<CalendarEventDto>(cancellationToken: ct)
              ?? throw new InvalidOperationException("Empty response from SetEventMembersAsync.");
    return MapToViewModel(dto);
}
```

Also update `GetCalendarsAsync` to use the new `EventCalendarDto` with `IsShared`:

```csharp
return dtos.Select(c => new CalendarSummaryViewModel(c.Id, c.DisplayName, c.Color, c.IsShared)).ToList();
```

Update `CalendarSummaryViewModel` to include `IsShared`:

```csharp
// src/FamilyHQ.WebUi/ViewModels/CalendarSummaryViewModel.cs
namespace FamilyHQ.WebUi.ViewModels;
public record CalendarSummaryViewModel(Guid Id, string DisplayName, string? Color, bool IsShared = false);
```

- [ ] **Step 12.2: Create SettingsCalendarsTab.razor**

```razor
@* src/FamilyHQ.WebUi/Components/Settings/SettingsCalendarsTab.razor *@
@using FamilyHQ.WebUi.Services
@using FamilyHQ.WebUi.ViewModels
@inject ICalendarApiService CalendarApi

<div class="settings-section">
    <h3 class="settings-section__title">Calendars</h3>
    <p class="settings-section__description">
        Control which calendars are shown on the kiosk and designate the shared calendar used for multi-member events.
    </p>

    @if (_calendars == null)
    {
        <div class="spinner" role="status"></div>
    }
    else if (_calendars.Count == 0)
    {
        <p>No calendars found. Sign in and sync to populate calendars.</p>
    }
    else
    {
        <ul class="calendar-settings-list">
            @foreach (var cal in _calendars)
            {
                <li class="calendar-settings-item glass-surface">
                    <span class="cal-color-dot" style="background-color: @(cal.Color ?? "#9e9e9e");"></span>
                    <span class="calendar-settings-item__name">@cal.DisplayName</span>

                    <label class="toggle-label" title="Show/hide this calendar">
                        <input type="checkbox"
                               checked="@_visibility[cal.Id]"
                               @onchange="e => OnVisibilityChanged(cal.Id, (bool)e.Value!)" />
                        Visible
                    </label>

                    <label class="toggle-label" title="Designate as shared calendar for multi-member events">
                        <input type="radio"
                               name="sharedCalendar"
                               checked="@_shared[cal.Id]"
                               @onchange="() => OnSharedChanged(cal.Id)" />
                        Shared
                    </label>
                </li>
            }
        </ul>

        @if (!string.IsNullOrEmpty(_error))
        {
            <div class="alert alert-danger mt-2">@_error</div>
        }
        @if (_saved)
        {
            <div class="alert alert-success mt-2">Saved.</div>
        }
    }
</div>

@code {
    private List<CalendarSummaryViewModel>? _calendars;
    private Dictionary<Guid, bool> _visibility = new();
    private Dictionary<Guid, bool> _shared = new();
    private string? _error;
    private bool _saved;

    protected override async Task OnInitializedAsync()
    {
        _calendars = (await CalendarApi.GetCalendarsAsync()).ToList();
        foreach (var cal in _calendars)
        {
            _visibility[cal.Id] = true; // loaded from server — extend CalendarSummaryViewModel if needed
            _shared[cal.Id]     = cal.IsShared;
        }
    }

    private async Task OnVisibilityChanged(Guid calId, bool isVisible)
    {
        _visibility[calId] = isVisible;
        _saved = false;
        _error = null;
        try
        {
            await CalendarApi.UpdateCalendarSettingsAsync(calId, isVisible, _shared[calId]);
            _saved = true;
        }
        catch
        {
            _error = "Failed to save. Please try again.";
        }
    }

    private async Task OnSharedChanged(Guid calId)
    {
        foreach (var key in _shared.Keys.ToList()) _shared[key] = false;
        _shared[calId] = true;
        _saved = false;
        _error = null;
        try
        {
            await CalendarApi.UpdateCalendarSettingsAsync(calId, _visibility[calId], true);
            _saved = true;
        }
        catch
        {
            _error = "Failed to save. Please try again.";
        }
    }
}
```

- [ ] **Step 12.3: Add Calendars tab to Settings.razor**

In `src/FamilyHQ.WebUi/Pages/Settings.razor`, add a new tab button after the "Display" tab:

```razor
<button class="settings-tab @(_activeTab == "calendars" ? "settings-tab--active" : "")"
        data-testid="tab-calendars"
        @onclick='() => SetTab("calendars")'>
    <span class="settings-tab__icon" aria-hidden="true">📅</span>
    <span class="settings-tab__label">Calendars</span>
</button>
```

And add the case in the switch:
```razor
case "calendars":
    <SettingsCalendarsTab />
    break;
```

- [ ] **Step 12.4: Build WebUi**

```bash
dotnet build src/FamilyHQ.WebUi
```

Expected: 0 errors.

- [ ] **Step 12.5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Settings/SettingsCalendarsTab.razor
git add src/FamilyHQ.WebUi/Pages/Settings.razor
git add src/FamilyHQ.WebUi/Services/
git add src/FamilyHQ.WebUi/ViewModels/CalendarSummaryViewModel.cs
git commit -m "feat(ui): add Calendar Settings tab for visibility toggle and shared calendar designation"
```

---

### Task 13: Agenda view column reorder

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor`

- [ ] **Step 13.1: Update AgendaView to support reorder mode**

Add reorder mode to `AgendaView.razor`. Key changes:
1. Add `[Parameter] public EventCallback<Dictionary<Guid, int>> OnSaveOrder { get; set; }` parameter
2. Add `_reorderMode` bool field
3. Add `Reorder` / `Cancel` / `Save` buttons
4. In reorder mode, render left/right arrow buttons in each column header; disable at boundaries

Modify the `<thead>` section of `AgendaView.razor`:

```razor
@* Agenda nav bar — add Reorder button *@
<div class="flex justify-between items-center mb-4">
    <div class="btn-group">
        <button class="btn btn-ghost" data-testid="agenda-prev-month" @onclick="OnPreviousMonth">&#8249; Prev</button>
        <button class="btn btn-glass" style="min-width: 150px; font-weight: 600;" data-testid="agenda-month-year-label" @onclick="OnOpenQuickJump">
            @CurrentMonth.ToString("MMMM yyyy")
        </button>
        <button class="btn btn-ghost" data-testid="agenda-next-month" @onclick="OnNextMonth">Next &#8250;</button>
    </div>
    <div class="flex gap-2" style="margin-left: 16px;">
        @if (!_reorderMode)
        {
            <button class="btn btn-secondary" data-testid="agenda-reorder-btn" @onclick="EnterReorderMode">Reorder</button>
            <button class="btn btn-primary" data-testid="agenda-create-button" @onclick="() => OnAddEvent.InvokeAsync(DateTime.Today)">+ Add Event</button>
        }
        else
        {
            <button class="btn btn-secondary" data-testid="agenda-reorder-cancel" @onclick="CancelReorderMode">Cancel</button>
            <button class="btn btn-primary" data-testid="agenda-reorder-save" @onclick="SaveOrder">Save Order</button>
        }
    </div>
</div>
```

Update the column header to show arrows in reorder mode:

```razor
<th data-testid="@($"agenda-calendar-header-{cal.Id}")" class="agenda-cal-col">
    @if (_reorderMode)
    {
        var idx = _reorderList.IndexOf(cal);
        <button class="btn btn-ghost btn-sm" data-testid="@($"agenda-reorder-left-{cal.Id}")"
                disabled="@(idx == 0)"
                @onclick="() => MoveLeft(idx)">&#8592;</button>
    }
    <span class="cal-color-dot" style="background-color: @(cal.Color ?? "#9e9e9e");"></span>
    @cal.DisplayName
    @if (_reorderMode)
    {
        var idx = _reorderList.IndexOf(cal);
        <button class="btn btn-ghost btn-sm" data-testid="@($"agenda-reorder-right-{cal.Id}")"
                disabled="@(idx == _reorderList.Count - 1)"
                @onclick="() => MoveRight(idx)">&#8594;</button>
    }
</th>
```

In the `@code` block, update the loop from `Calendars` to `_reorderList` so the rendered columns reflect the current reorder state:

```csharp
private bool _reorderMode;
private List<CalendarSummaryViewModel> _reorderList = new();

private void EnterReorderMode()
{
    _reorderList = Calendars.ToList();
    _reorderMode = true;
}

private void CancelReorderMode()
{
    _reorderMode = false;
}

private void MoveLeft(int index)
{
    if (index <= 0) return;
    (_reorderList[index - 1], _reorderList[index]) = (_reorderList[index], _reorderList[index - 1]);
}

private void MoveRight(int index)
{
    if (index >= _reorderList.Count - 1) return;
    (_reorderList[index], _reorderList[index + 1]) = (_reorderList[index + 1], _reorderList[index]);
}

private async Task SaveOrder()
{
    var order = _reorderList
        .Select((cal, i) => (cal.Id, DisplayOrder: i))
        .ToDictionary(x => x.Id, x => x.DisplayOrder);
    await OnSaveOrder.InvokeAsync(order);
    _reorderMode = false;
}
```

Replace `@foreach (var cal in Calendars)` in the `<thead>` with `@foreach (var cal in (_reorderMode ? _reorderList : Calendars))`.

Add `[Parameter] public EventCallback<Dictionary<Guid, int>> OnSaveOrder { get; set; }` to parameters.

- [ ] **Step 13.2: Wire up OnSaveOrder in the parent (Index.razor or Dashboard page)**

Find where `AgendaView` is used (in `src/FamilyHQ.WebUi/Pages/Index.razor` or equivalent) and add:

```razor
<AgendaView ... OnSaveOrder="HandleSaveOrder" />
```

```csharp
private async Task HandleSaveOrder(Dictionary<Guid, int> order)
{
    await CalendarApi.SaveCalendarOrderAsync(order);
    await LoadCalendars(); // refresh calendar list from API to reflect new order
}
```

- [ ] **Step 13.3: Build and verify**

```bash
dotnet build src/FamilyHQ.WebUi
```

Expected: 0 errors.

- [ ] **Step 13.4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat(ui): add Reorder mode to AgendaView with left/right column arrows and Save"
```

---

### Task 14: EventModal — description field and member chip refactor

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor`
- Modify: `src/FamilyHQ.WebUi/Components/CalendarChipSelector.razor`

- [ ] **Step 14.1: Add description field to EventModal**

In `EventModal.razor`, add after the Location field (before the error div):

```razor
<div class="mb-3">
    <label class="form-label">Description (Optional)</label>
    <textarea class="form-input" rows="3" @bind="_eventModel.Description"
              placeholder="Notes, details…"></textarea>
</div>
```

Update `EventFormModel` to expose `Description`:
```csharp
public string? Description { get; set; }
```

Update `OpenForEdit` to load description:
```csharp
Description = evt.Description
```

Update `SaveEvent` to pass description:
```csharp
// In create path:
var createReq = new CreateEventRequest(
    _selectedCalendarIds.ToList(),
    _eventModel.Title,
    _eventModel.Start,
    _eventModel.End,
    _eventModel.IsAllDay,
    _eventModel.Location,
    _eventModel.Description);   // ← add

// In update path:
var updateReq = new UpdateEventRequest(
    _eventModel.Title,
    _eventModel.Start,
    _eventModel.End,
    _eventModel.IsAllDay,
    _eventModel.Location,
    _eventModel.Description);   // ← add
```

- [ ] **Step 14.2: Update CalendarChipSelector to call SetMembers instead of Add/Remove**

The chip selector in edit mode should now call `SetEventMembersAsync` (replacing the full member list) instead of individual `AddCalendarToEventAsync` / `RemoveCalendarFromEventAsync`.

In `CalendarChipSelector.razor`, the `ToggleCalendarAsync` edit-mode path becomes:

```csharp
// Edit mode — replace full member list
if (CalendarApi is null) return;

var newIds = new HashSet<Guid>(SelectedCalendarIds);

if (isActive)
{
    if (newIds.Count <= 1) return; // protected
    newIds.Remove(cal.Id);
}
else
{
    newIds.Add(cal.Id);
}

try
{
    await CalendarApi.SetEventMembersAsync(EventId!.Value, newIds.ToList());
    SelectedCalendarIds = newIds;
    await SelectedCalendarIdsChanged.InvokeAsync(SelectedCalendarIds);
}
catch (Exception ex) when (ex is not OperationCanceledException and not TaskCanceledException)
{
    _chipError = $"Could not update members. Please try again.";
}
```

- [ ] **Step 14.3: Update AgendaView and MonthView to exclude shared calendar from column list**

In the parent component that passes `Calendars` to `AgendaView`, filter out shared calendars:

```csharp
_displayCalendars = _allCalendars.Where(c => !c.IsShared && c.IsVisible).ToList();
```

- [ ] **Step 14.4: Build**

```bash
dotnet build src/FamilyHQ.WebUi
```

Expected: 0 errors.

- [ ] **Step 14.5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor
git add src/FamilyHQ.WebUi/Components/CalendarChipSelector.razor
git commit -m "feat(ui): add description field to EventModal; update chip selector to use SetMembers API"
```

---

### Task 15: Remove ICalendarApiService members that no longer exist and final build

- [ ] **Step 15.1: Remove AddCalendarToEventAsync and RemoveCalendarFromEventAsync from ICalendarApiService**

Remove these two method signatures from `ICalendarApiService.cs` and their implementations from `CalendarApiService.cs`. Verify no Blazor components reference them (CalendarChipSelector now calls `SetEventMembersAsync`).

- [ ] **Step 15.2: Full solution build**

```bash
dotnet build FamilyHQ.sln
```

Expected: 0 errors, 0 warnings that indicate broken references.

- [ ] **Step 15.3: Run all unit tests**

```bash
dotnet test tests/ -v n 2>&1 | tail -30
```

Expected: all pass.

- [ ] **Step 15.4: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/
git commit -m "refactor(ui): remove obsolete AddCalendarToEvent / RemoveCalendarFromEvent API calls"
```

---

### Task 16: Update E2E tests

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json`
- Modify: `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature`
- Modify: `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature`
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs`
- Create: `tests-e2e/FamilyHQ.E2E.Features/CalendarSettings.feature`

- [ ] **Step 16.1: Update user_templates.json — add isShared and shared calendar**

The existing `TestFamilyMember` template should be updated:
1. Add `"isShared": true` to one of the calendar entries (add a dedicated shared calendar entry).
2. Existing individual calendar entries get `"isShared": false`.

```json
{
    "TestFamilyMember": {
        "Calendars": [
            { "Id": "cal_eoin", "Summary": "Eoin", "BackgroundColor": "#4285f4", "IsShared": false },
            { "Id": "cal_sarah", "Summary": "Sarah", "BackgroundColor": "#34a853", "IsShared": false },
            { "Id": "cal_shared", "Summary": "Family Shared", "BackgroundColor": "#ea4335", "IsShared": true }
        ],
        "Events": []
    }
}
```

- [ ] **Step 16.2: Update SimulatorApiClient models to include IsShared**

In whatever model class represents a calendar in the E2E data layer, add `bool IsShared`.

- [ ] **Step 16.3: Update Dashboard.feature — multi-member scenarios**

Existing scenarios that test "Create event in two calendars appears twice on grid" need to be updated to reflect the new model:
- Events assigned to multiple members now show on the shared calendar in Google, but appear in each member's column on the kiosk.
- The step "I create an event for calendars Eoin and Sarah" should now produce one event on the shared calendar that appears in BOTH the Eoin and Sarah columns.

Update these scenarios:
- "Create event in two calendars appears twice on grid" — verify event appears in both member columns
- "Add calendar to existing event via chip" → "Add member to existing event via chip"
- "Remove calendar chip from event" → "Remove member chip from event"
- "Last chip is protected — cannot remove final member" (wording update only)

- [ ] **Step 16.4: Update GoogleCalendarSync.feature — member tag scenarios**

Add a new scenario:

```gherkin
Scenario: Event with member tag in description appears in correct member columns
  Given I have a user like "TestFamilyMember"
  And the simulator has an event on the shared calendar with description "[members: Eoin, Sarah]" titled "Family dinner"
  When the webhook fires
  And I view the dashboard
  Then I see "Family dinner" in the Eoin column
  And I see "Family dinner" in the Sarah column
  And I do not see "Family dinner" in any "Family Shared" column
```

Add a scenario for natural language fallback:

```gherkin
Scenario: Event with natural language member mention appears in correct column
  Given I have a user like "TestFamilyMember"
  And the simulator has an event on the shared calendar with description "Eoin collecting kids from school" titled "School run"
  When the webhook fires
  And I view the dashboard
  Then I see "School run" in the Eoin column
```

- [ ] **Step 16.5: Create CalendarSettings.feature**

```gherkin
Feature: Calendar Settings
  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in

  Scenario: Toggle calendar visibility
    When I navigate to settings
    And I open the Calendars tab
    And I uncheck visibility for "Eoin"
    Then the "Eoin" column is not visible on the Agenda view

  Scenario: Designate a shared calendar
    When I navigate to settings
    And I open the Calendars tab
    And I select "Family Shared" as the shared calendar
    Then events created for multiple members are placed on "Family Shared" in Google

  Scenario: Reorder calendar columns
    When I view the Agenda view
    And I click Reorder
    And I click the right arrow on "Eoin"
    And I click Save Order
    Then the "Sarah" column appears before the "Eoin" column
```

- [ ] **Step 16.6: Update EventSteps.cs — add member-tag event seeding**

Add a step to seed an event with a description containing a member tag:

```csharp
[Given(@"the simulator has an event on the shared calendar with description ""([^""]*)"" titled ""([^""]*)""")]
public async Task GivenSimulatorHasSharedEventWithDescription(string description, string title)
{
    var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var sharedCal = template.Calendars.First(c => c.IsShared);

    var request = new BackdoorEventRequest
    {
        UserId      = template.UserName,
        CalendarId  = sharedCal.Id,
        Summary     = title,
        Description = description,
        Start       = DateTime.UtcNow.Date.AddHours(10),
        End         = DateTime.UtcNow.Date.AddHours(11),
        IsAllDay    = false
    };

    await _simulatorApi.AddBackdoorEventAsync(request);
}
```

- [ ] **Step 16.7: Add DashboardPage locators for Agenda member columns**

In `tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs`, add locators to check column visibility and event presence in a specific member's column:

```csharp
public ILocator AgendaColumnHeader(string calendarName) =>
    Page.Locator($"[data-testid^='agenda-calendar-header-']")
        .Filter(new() { HasText = calendarName });

public ILocator EventInColumn(string eventTitle, string calendarId) =>
    Page.Locator($"[data-testid^='agenda-event-'][data-testid*='{calendarId}']")
        .Filter(new() { HasText = eventTitle });
```

- [ ] **Step 16.8: Run the E2E tests**

Start services (WebApi, Simulator, WebUi) then run:

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test --logger "console;verbosity=detailed" 2>&1 | tail -50
```

Fix any failures. Common issues:
- Locator selectors referencing old `agenda-event-{eventId}-{calendarId}` data-testid format — check AgendaView.razor renders these correctly for projected events
- Step definition missing for new Gherkin steps — add binding

- [ ] **Step 16.9: Commit**

```bash
git add tests-e2e/
git commit -m "test(e2e): update E2E tests for member-tag model; add CalendarSettings.feature; add shared-event member projection scenarios"
```

---

## Self-Review Checklist

### Spec Coverage

| Architecture Doc Item | Covered In |
|---|---|
| 1. Google OAuth — no changes needed (scope already covers extended properties) | N/A |
| 2. Calendar sync service | Task 8 (CalendarSyncService) |
| 3. Webhook / push notification handler | Task 8 (CalendarSyncService + existing SyncController) |
| 4. Calendar migration service | Task 5 |
| 5. Member tag parser | Task 3 |
| 6. Database schema — Events, Members, EventMembers | Task 1 |
| 7. Calendar Settings tab — shared calendar + visibility | Tasks 10, 12 |
| 8. Monthly agenda view reorder | Task 13 |
| 9. Kiosk event create/edit — pill selector + description field | Task 14 |
| 10. Kiosk calendar display — projection, shared calendar hidden | Tasks 10, 14 |
| Webhook echo prevention (content-hash) | Tasks 4, 6, 8 |

### Gaps Identified and Addressed
- `UpdateEventRequest.Description` — already existed, no change needed.
- `GetEventsByOwnerCalendarAsync` replaces the old per-calendar signature (`GetEventsAsync(Guid, ...)`) in `ICalendarRepository`; `CalendarSyncService.SyncAsync` references this.
- The `SyncOrchestrator` background service still calls `SyncAllAsync` — unchanged; no update needed.
- `BackgroundUserContext.Current` in `SyncController.GooglePushWebhook` — unchanged; still required for webhook context.
- `EventCalendarDto.IsShared` is a defaulted parameter (`= false`) so existing callers compile without update.
- `IMemberTagParser` injected into `CalendarsController` could be cleaner — an inline regex is used in Task 10 to avoid controller complexity. Can be refactored later.
- `CalendarInfo.Events` navigation was removed in Task 1; `CalendarInfoConfiguration` no longer configures a `WithMany(c => c.Events)` back-navigation.
