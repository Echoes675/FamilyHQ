# Event Multi-Calendar Support — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend FamilyHQ so that a calendar event can belong to multiple Google Calendars simultaneously, appearing once per calendar on the month grid in that calendar's colour, with a chip-based UI for adding and removing calendars from an event.

**Architecture:** Events remain a single row in the DB (one `GoogleEventId`); multi-calendar membership uses the existing `CalendarEventCalendar` join table plus Google's attendee model. New `OwnerCalendarInfoId` tracks the Google organiser for write operations. A strict 5-layer boundary (Google API types → domain models → API DTOs → ViewModels → Blazor) is maintained throughout.

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core / PostgreSQL, xUnit + Moq + FluentAssertions, Google Calendar API (simulator for dev/test).

---

## File Map

### New files
| File | Responsibility |
|------|----------------|
| `tools/FamilyHQ.Simulator/Models/SimulatedEventAttendee.cs` | Simulator attendee table model |
| `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs` | Internal Google API response records |
| `src/FamilyHQ.Core/Models/GoogleEventDetail.cs` | Domain result type for `GetEventAsync` |
| `src/FamilyHQ.Core/Validators/UpdateEventRequestValidator.cs` | Validator for `UpdateEventRequest` |
| `src/FamilyHQ.WebApi/Controllers/EventsController.cs` | 5 event-centric endpoints |
| `src/FamilyHQ.WebUi/ViewModels/CalendarSummaryViewModel.cs` | Lightweight calendar summary for chips |
| `src/FamilyHQ.WebUi/ViewModels/MonthViewModel.cs` | Blazor-layer month view |
| `src/FamilyHQ.WebUi/ViewModels/CalendarEventViewModel.cs` | Replaces `FamilyHQ.Core` version |

### Modified files
| File | Change |
|------|--------|
| `tools/FamilyHQ.Simulator/Models/SimulatedEvent.cs` | No change to model; response shape updated in controller |
| `tools/FamilyHQ.Simulator/Data/SimContext.cs` | Add `EventAttendees` DbSet + EF config |
| `tools/FamilyHQ.Simulator/Controllers/EventsController.cs` | Add `PATCH /{eventId}`, `GET /{eventId}`; update list filter + all response shapes |
| `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs` | Rewrite to use `GoogleApiTypes`; add `PatchEventAttendeesAsync` + `GetEventAsync` |
| `src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs` | Add `PatchEventAttendeesAsync` + `GetEventAsync` |
| `src/FamilyHQ.Core/Models/CalendarEvent.cs` | Add `OwnerCalendarInfoId` + `IsExternallyOwned` |
| `src/FamilyHQ.Data.PostgreSQL/Configurations/CalendarEventConfiguration.cs` | Add FK + `HasDefaultValue` config |
| `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs` | `CalendarInfoId → IReadOnlyList<Guid> CalendarInfoIds` |
| `src/FamilyHQ.Core/DTOs/UpdateEventRequest.cs` | Remove `CalendarInfoId` |
| `src/FamilyHQ.Core/DTOs/CalendarEventDto.cs` | Replace `CalendarInfoId` + `CalendarColor` with `IReadOnlyList<EventCalendarDto> Calendars` |
| `src/FamilyHQ.Core/DTOs/MonthViewDto.cs` | `Days` → `Dictionary<string, List<CalendarEventDto>>` |
| `src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs` | Validate `CalendarInfoIds` list |
| `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs` | Replace `ReassignAsync` with 5 new methods |
| `src/FamilyHQ.Services/Calendar/CalendarEventService.cs` | Full rewrite |
| `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs` | Populate `OwnerCalendarInfoId` + `IsExternallyOwned`; second-calendar attach |
| `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs` | Keep only `GET /api/calendars` + `GET /api/calendars/events`; fix DTO leakage |
| `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs` | Replace old methods with new signatures; return ViewModels |
| `src/FamilyHQ.WebUi/Services/CalendarApiService.cs` | Full rewrite with ViewModel mapping + grid expansion |
| `tests/FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs` | Add attendee tests |
| `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs` | Replace with 5-method tests |
| `tests/FamilyHQ.WebApi.Tests/Controllers/CalendarsControllerTests.cs` | Update for DTO changes |
| `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature` | Add multi-calendar BDD scenarios |

### Deleted files
| File | Why |
|------|-----|
| `src/FamilyHQ.Core/ViewModels/CalendarEventViewModel.cs` | Moved to `FamilyHQ.WebUi` |

---

## Task 1 — Simulator: attendee table + updated endpoints

**Files:**
- Create: `tools/FamilyHQ.Simulator/Models/SimulatedEventAttendee.cs`
- Modify: `tools/FamilyHQ.Simulator/Data/SimContext.cs`
- Modify: `tools/FamilyHQ.Simulator/Controllers/EventsController.cs`
- Test: `tests/FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs`

### Why this is first

The simulator is the foundation for all `GoogleCalendarClient` integration tests. Getting the correct Google API response shape (with `organizer` and `attendees`) in place first means every downstream test exercises real deserialization.

### Step 1.1 — Write failing tests for new simulator behaviour

Add these tests to `tests/FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs`:

```csharp
// ── ListEvents — attendee filter ──────────────────────────────────────────

[Fact]
public async Task ListEvents_ReturnsEventWhereCalendarIsAttendee()
{
    using var db = CreateDb();
    db.Events.Add(new SimulatedEvent
        { Id = "evt-1", CalendarId = "cal-organiser", Summary = "Multi", UserId = "alice" });
    db.EventAttendees.Add(new SimulatedEventAttendee
        { EventId = "evt-1", AttendeeCalendarId = "cal-attendee" });
    await db.SaveChangesAsync();

    var sut = CreateSut(db, userId: "alice");
    var result = await sut.ListEvents("cal-attendee");

    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var json = JsonSerializer.Serialize(ok.Value);
    json.Should().Contain("evt-1");
}

[Fact]
public async Task ListEvents_ResponseIncludesOrganizerAndAttendees()
{
    using var db = CreateDb();
    db.Events.Add(new SimulatedEvent
        { Id = "evt-1", CalendarId = "cal-org", Summary = "With Attendee", UserId = "alice" });
    db.EventAttendees.Add(new SimulatedEventAttendee
        { EventId = "evt-1", AttendeeCalendarId = "cal-att" });
    await db.SaveChangesAsync();

    var sut = CreateSut(db, userId: "alice");
    var result = await sut.ListEvents("cal-org");

    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var json = JsonSerializer.Serialize(ok.Value);
    json.Should().Contain("\"organizer\"");
    json.Should().Contain("cal-org");
    json.Should().Contain("\"attendees\"");
    json.Should().Contain("cal-att");
}

// ── GetEvent (new) ───────────────────────────────────────────────────────

[Fact]
public async Task GetEvent_ReturnsEventWithOrganizerAndAttendees()
{
    using var db = CreateDb();
    db.Events.Add(new SimulatedEvent
        { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
    db.EventAttendees.Add(new SimulatedEventAttendee
        { EventId = "evt-1", AttendeeCalendarId = "cal-att" });
    await db.SaveChangesAsync();

    var sut = CreateSut(db, userId: "alice");
    var result = await sut.GetEvent("cal-org", "evt-1");

    var ok = result.Should().BeOfType<OkObjectResult>().Subject;
    var json = JsonSerializer.Serialize(ok.Value);
    json.Should().Contain("evt-1");
    json.Should().Contain("\"organizer\"");
    json.Should().Contain("\"attendees\"");
    json.Should().Contain("cal-att");
}

[Fact]
public async Task GetEvent_Returns404WhenNotFound()
{
    using var db = CreateDb();
    var sut = CreateSut(db, userId: "alice");
    var result = await sut.GetEvent("cal-org", "no-such-event");
    result.Should().BeOfType<NotFoundObjectResult>();
}

// ── PatchEvent (new) ──────────────────────────────────────────────────────

[Fact]
public async Task PatchEvent_ReplacesAttendees()
{
    using var db = CreateDb();
    db.Events.Add(new SimulatedEvent
        { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
    db.EventAttendees.AddRange(
        new SimulatedEventAttendee { EventId = "evt-1", AttendeeCalendarId = "cal-old" });
    await db.SaveChangesAsync();

    var sut = CreateSut(db, userId: "alice");
    var body = new SimulatorPatchAttendeesRequest(
        new[] { new SimulatorAttendee("cal-new") });
    var result = await sut.PatchEvent("cal-org", "evt-1", body);

    result.Should().BeOfType<OkObjectResult>();
    var attendees = await db.EventAttendees.Where(a => a.EventId == "evt-1").ToListAsync();
    attendees.Should().ContainSingle(a => a.AttendeeCalendarId == "cal-new");
    attendees.Should().NotContain(a => a.AttendeeCalendarId == "cal-old");
}

[Fact]
public async Task PatchEvent_DoesNotAddOrganizerToAttendeesTable()
{
    using var db = CreateDb();
    db.Events.Add(new SimulatedEvent
        { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
    await db.SaveChangesAsync();

    var sut = CreateSut(db, userId: "alice");
    // Include organiser in the attendees payload — it must be silently excluded
    var body = new SimulatorPatchAttendeesRequest(
        new[] { new SimulatorAttendee("cal-org"), new SimulatorAttendee("cal-att") });
    await sut.PatchEvent("cal-org", "evt-1", body);

    var attendees = await db.EventAttendees.Where(a => a.EventId == "evt-1").ToListAsync();
    attendees.Should().NotContain(a => a.AttendeeCalendarId == "cal-org");
    attendees.Should().ContainSingle(a => a.AttendeeCalendarId == "cal-att");
}
```

- [ ] **Step 1.1: Run tests to confirm they fail**

```
dotnet test tests/FamilyHQ.Simulator.Tests --filter "FullyQualifiedName~EventsControllerTests"
```

Expected: compile error or missing type errors.

### Step 1.2 — Create `SimulatedEventAttendee`

Create `tools/FamilyHQ.Simulator/Models/SimulatedEventAttendee.cs`:

```csharp
namespace FamilyHQ.Simulator.Models;

public class SimulatedEventAttendee
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string AttendeeCalendarId { get; set; } = string.Empty;
}
```

- [ ] **Step 1.2: Create `SimulatedEventAttendee`**

### Step 1.3 — Create patch/attendee DTOs

Add `SimulatorAttendee` and `SimulatorPatchAttendeesRequest` to `tools/FamilyHQ.Simulator/DTOs/`. Check the existing DTOs file first:

Create `tools/FamilyHQ.Simulator/DTOs/SimulatorPatchAttendeesRequest.cs`:

```csharp
namespace FamilyHQ.Simulator.DTOs;

public record SimulatorAttendee(string Email);
public record SimulatorPatchAttendeesRequest(IReadOnlyList<SimulatorAttendee> Attendees);
```

- [ ] **Step 1.3: Create patch DTOs**

### Step 1.4 — Update `SimContext`

Modify `tools/FamilyHQ.Simulator/Data/SimContext.cs` — add `EventAttendees` DbSet and its EF config inside `OnModelCreating`:

```csharp
public DbSet<SimulatedEventAttendee> EventAttendees => Set<SimulatedEventAttendee>();
```

Inside `OnModelCreating`, after the `SimulatedUser` block:

```csharp
modelBuilder.Entity<SimulatedEventAttendee>(entity =>
{
    entity.ToTable("SimulatedEventAttendees");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.EventId).IsRequired().HasMaxLength(255);
    entity.Property(e => e.AttendeeCalendarId).IsRequired().HasMaxLength(255);
    entity.HasIndex(e => new { e.EventId, e.AttendeeCalendarId }).IsUnique();
});
```

- [ ] **Step 1.4: Update `SimContext`**

### Step 1.5 — Add simulator EF migration

```
dotnet ef migrations add AddEventAttendees --project tools/FamilyHQ.Simulator --startup-project tools/FamilyHQ.Simulator
```

Expected: new migration file in `tools/FamilyHQ.Simulator/Migrations/`.

- [ ] **Step 1.5: Add simulator migration**

### Step 1.6 — Update `EventsController` in simulator

Modify `tools/FamilyHQ.Simulator/Controllers/EventsController.cs`:

**`ListEvents`** — update the query and response shape:

```csharp
var events = await _db.Events
    .Where(e => e.UserId == userId &&
                (e.CalendarId == calendarId ||
                 _db.EventAttendees.Any(a => a.EventId == e.Id && a.AttendeeCalendarId == calendarId)))
    .ToListAsync();

// Helper: build attendees for each event in the list
var eventIds = events.Select(e => e.Id).ToList();
var attendeesByEvent = await _db.EventAttendees
    .Where(a => eventIds.Contains(a.EventId))
    .GroupBy(a => a.EventId)
    .ToDictionaryAsync(g => g.Key, g => g.Select(a => a.AttendeeCalendarId).ToList());
```

Response shape for each event in `ListEvents`, `CreateEvent`, `UpdateEvent`, `MoveEvent`, `DeleteEvent` (the 200 OK variants) — add `organizer` and `attendees`:

```csharp
private static object MapEventResponse(SimulatedEvent e, IReadOnlyList<string> attendeeCalendarIds) => new
{
    id = e.Id,
    status = "confirmed",
    summary = e.Summary,
    location = e.Location,
    description = e.Description,
    start = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
    end   = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd")   } : new { dateTime = e.EndTime.ToString("O")   },
    organizer = new { email = e.CalendarId, self = true },
    attendees = attendeeCalendarIds.Count > 0
        ? (object)attendeeCalendarIds.Select(cal => new { email = cal, responseStatus = "accepted" }).ToArray()
        : null
};
```

Add `GET /{eventId}` endpoint:

```csharp
[HttpGet("{eventId}")]
public async Task<IActionResult> GetEvent(string calendarId, string eventId)
{
    _logger.LogInformation("[SIM] GET event: {EventId} for calendar: {CalendarId}", eventId, calendarId);
    var userId = ExtractUserId(Request);

    var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
    if (existing == null)
    {
        _logger.LogWarning("[SIM] Event {EventId} not found for GET.", eventId);
        return NotFound(new
        {
            error = new { code = 404, message = "Not Found",
                errors = new[] { new { domain = "calendar", reason = "notFound", message = "Not Found" } } }
        });
    }

    var attendees = await _db.EventAttendees
        .Where(a => a.EventId == eventId)
        .Select(a => a.AttendeeCalendarId)
        .ToListAsync();

    return Ok(MapEventResponse(existing, attendees));
}
```

Add `PATCH /{eventId}` endpoint:

```csharp
[HttpPatch("{eventId}")]
public async Task<IActionResult> PatchEvent(string calendarId, string eventId,
    [FromBody] SimulatorPatchAttendeesRequest body)
{
    _logger.LogInformation("[SIM] PATCH event: {EventId} for calendar: {CalendarId}", eventId, calendarId);
    var userId = ExtractUserId(Request);

    var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
    if (existing == null)
    {
        _logger.LogWarning("[SIM] Event {EventId} not found for patch.", eventId);
        return NotFound(new
        {
            error = new { code = 404, message = "Not Found",
                errors = new[] { new { domain = "calendar", reason = "notFound", message = "Not Found" } } }
        });
    }

    // Full replacement — remove existing attendees for this event
    var existing_attendees = _db.EventAttendees.Where(a => a.EventId == eventId);
    _db.EventAttendees.RemoveRange(existing_attendees);

    // Add new attendees, excluding the organiser
    var newAttendees = (body?.Attendees ?? Array.Empty<SimulatorAttendee>())
        .Where(a => a.Email != existing.CalendarId)
        .Select(a => new SimulatedEventAttendee { EventId = eventId, AttendeeCalendarId = a.Email });
    await _db.EventAttendees.AddRangeAsync(newAttendees);
    await _db.SaveChangesAsync();

    var attendeeIds = await _db.EventAttendees
        .Where(a => a.EventId == eventId)
        .Select(a => a.AttendeeCalendarId)
        .ToListAsync();

    _logger.LogInformation("[SIM] Patched attendees for event: {EventId}", eventId);
    return Ok(MapEventResponse(existing, attendeeIds));
}
```

Update `CreateSut` in the test class to use the updated `EventAttendees` DbSet (the in-memory db from `CreateDb()` already gets it from the model, so no change needed there if `SimContext` is updated).

- [ ] **Step 1.6: Update simulator `EventsController`**

### Step 1.7 — Run tests

```
dotnet test tests/FamilyHQ.Simulator.Tests --filter "FullyQualifiedName~EventsControllerTests"
```

Expected: all tests pass (including existing tests).

- [ ] **Step 1.7: Confirm all simulator controller tests pass**

### Step 1.8 — Commit

```bash
git add tools/FamilyHQ.Simulator/
tests/FamilyHQ.Simulator.Tests/Controllers/EventsControllerTests.cs
git commit -m "feat(simulator): add attendee table, PATCH/GET endpoints, organizer+attendee response shape"
```

- [ ] **Step 1.8: Commit**

---

## Task 2 — Google API internal types

**Files:**
- Create: `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs`

These records mirror the Google Calendar API JSON exactly. They replace the anonymous types currently used inside `GoogleCalendarClient` and are the deserialization target for all HTTP responses. They are `internal` — they must not appear outside `FamilyHQ.Services.Calendar.GoogleApi`.

- [ ] **Step 2.1: Create `src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs`**

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

internal record GoogleApiAttendee(
    [property: JsonPropertyName("email")]          string  Email,
    [property: JsonPropertyName("responseStatus")] string? ResponseStatus);

internal record GoogleApiEvent(
    [property: JsonPropertyName("id")]          string               Id,
    [property: JsonPropertyName("iCalUID")]     string?              ICalUID,
    [property: JsonPropertyName("status")]      string?              Status,
    [property: JsonPropertyName("summary")]     string?              Summary,
    [property: JsonPropertyName("description")] string?              Description,
    [property: JsonPropertyName("location")]    string?              Location,
    [property: JsonPropertyName("start")]       GoogleApiEventDateTime? Start,
    [property: JsonPropertyName("end")]         GoogleApiEventDateTime? End,
    [property: JsonPropertyName("organizer")]   GoogleApiOrganizer?  Organizer,
    [property: JsonPropertyName("attendees")]   IReadOnlyList<GoogleApiAttendee>? Attendees);

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

There are no unit tests for these records themselves — they are data shapes. The `GoogleCalendarClient` mapping tests in Task 4 exercise them indirectly.

- [ ] **Step 2.2: Build to confirm no compile errors**

```
dotnet build src/FamilyHQ.Services/FamilyHQ.Services.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2.3: Commit**

```bash
git add src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs
git commit -m "feat(google-client): add internal GoogleApi response types"
```

---

## Task 3 — Domain model + EF config + migration

**Files:**
- Modify: `src/FamilyHQ.Core/Models/CalendarEvent.cs`
- Create: `src/FamilyHQ.Core/Models/GoogleEventDetail.cs`
- Modify: `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs`
- New migration in `src/FamilyHQ.Data.PostgreSQL/Migrations/`

### Step 3.1 — Update `CalendarEvent`

Modify `src/FamilyHQ.Core/Models/CalendarEvent.cs`:

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

    // FK to the CalendarInfo that is the Google organiser for this event.
    // Used to select the correct calendarId for events.update, events.move, events.delete.
    public Guid OwnerCalendarInfoId { get; set; }

    // True when Google's organizer.Self = false at sync time. Informational only.
    public bool IsExternallyOwned { get; set; }

    // Navigation properties
    public ICollection<CalendarInfo> Calendars { get; set; } = new List<CalendarInfo>();
}
```

- [ ] **Step 3.1: Update `CalendarEvent`**

### Step 3.2 — Create `GoogleEventDetail`

Create `src/FamilyHQ.Core/Models/GoogleEventDetail.cs`:

```csharp
namespace FamilyHQ.Core.Models;

/// <summary>
/// Lightweight result of IGoogleCalendarClient.GetEventAsync.
/// Used only by the service layer to perform the external-attendee check before delete.
/// Blazor components must not use this type.
/// </summary>
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    IReadOnlyList<string> AttendeeEmails);
```

- [ ] **Step 3.2: Create `GoogleEventDetail`**

### Step 3.3 — Update `CalendarEventConfiguration`

Modify `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs` — add inside the `Configure` method, after the existing index config:

```csharp
builder.Property(e => e.OwnerCalendarInfoId)
    .IsRequired();

builder.HasOne<CalendarInfo>()
    .WithMany()
    .HasForeignKey(e => e.OwnerCalendarInfoId)
    .OnDelete(DeleteBehavior.Restrict);

builder.Property(e => e.IsExternallyOwned)
    .IsRequired()
    .HasDefaultValue(false);
```

- [ ] **Step 3.3: Update `CalendarEventConfiguration`**

### Step 3.4 — Add EF migration

```
dotnet ef migrations add AddOwnerCalendarInfoId --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```

Expected: new migration file created. Open it and **verify** the `Up` method contains:
1. `AddColumn` for `OwnerCalendarInfoId uuid NOT NULL DEFAULT '00000000-...'`
2. SQL backfill:
   ```sql
   migrationBuilder.Sql("""
       UPDATE "Events" e
       SET "OwnerCalendarInfoId" = (
           SELECT cec."CalendarsId"
           FROM "CalendarEventCalendar" cec
           WHERE cec."EventsId" = e."Id"
           ORDER BY cec."CalendarsId"
           LIMIT 1
       );
   """);
   ```
3. `AddForeignKey` for `OwnerCalendarInfoId → Calendars.Id RESTRICT`
4. `AddColumn` for `IsExternallyOwned boolean NOT NULL DEFAULT false`
5. `DropColumn` default on `OwnerCalendarInfoId`

**Important:** EF Core will generate step 1 automatically, but steps 2–5 need manual additions. Edit the generated migration `Up` method to add the following in this order:

After `AddColumn` for `OwnerCalendarInfoId`, insert the backfill:
```csharp
migrationBuilder.Sql("""
    UPDATE "Events" e
    SET "OwnerCalendarInfoId" = (
        SELECT cec."CalendarsId"
        FROM "CalendarEventCalendar" cec
        WHERE cec."EventsId" = e."Id"
        ORDER BY cec."CalendarsId"
        LIMIT 1
    );
""");
```

After the FK is added, drop the temporary DEFAULT (critical — prevents zero-UUID default persisting on future inserts):
```csharp
migrationBuilder.Sql("""
    ALTER TABLE "Events" ALTER COLUMN "OwnerCalendarInfoId" DROP DEFAULT;
""");
```

Also add `AddColumn` for `IsExternallyOwned` with `defaultValue: false` if EF Core does not generate it automatically.

- [ ] **Step 3.4: Add and edit migration to include backfill SQL**

### Step 3.5 — Build

```
dotnet build src/FamilyHQ.Core/FamilyHQ.Core.csproj
dotnet build src/FamilyHQ.Data.PostgreSQL/FamilyHQ.Data.PostgreSQL.csproj
```

Expected: both succeed.

- [ ] **Step 3.5: Build succeeds**

### Step 3.6 — Commit

```bash
git add src/FamilyHQ.Core/Models/
src/FamilyHQ.Data.PostgreSQL/Configurations/CalendarEventConfiguration.cs
src/FamilyHQ.Data.PostgreSQL/Migrations/
git commit -m "feat(domain): add OwnerCalendarInfoId, IsExternallyOwned, GoogleEventDetail, EF migration"
```

- [ ] **Step 3.6: Commit**

---

## Task 4 — `IGoogleCalendarClient` additions + `GoogleCalendarClient` rewrite

**Files:**
- Modify: `src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs`
- Modify: `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs`
- Test: `tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientMappingTests.cs` (new)

### Step 4.1 — Write failing mapping tests

Create `tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientMappingTests.cs`:

```csharp
using FluentAssertions;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Calendar.GoogleApi;
using Moq;
using Microsoft.Extensions.Logging;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace FamilyHQ.Services.Tests.Calendar;

public class GoogleCalendarClientMappingTests
{
    // These tests verify that GoogleCalendarClient correctly maps
    // GoogleApiEvent → domain models and GoogleEventDetail.
    // We test the mapping through the public interface by injecting fake HTTP responses.

    [Fact]
    public async Task GetEventAsync_MapsOrganizerAndAttendeesToGoogleEventDetail()
    {
        var apiEvent = new GoogleApiEvent(
            Id: "evt-123",
            ICalUID: null,
            Status: "confirmed",
            Summary: "Team Meeting",
            Description: null,
            Location: null,
            Start: new GoogleApiEventDateTime(DateTimeOffset.UtcNow, null, null),
            End:   new GoogleApiEventDateTime(DateTimeOffset.UtcNow.AddHours(1), null, null),
            Organizer: new GoogleApiOrganizer("org@calendar.google.com", Self: true),
            Attendees: new[]
            {
                new GoogleApiAttendee("att1@calendar.google.com", "accepted"),
                new GoogleApiAttendee("att2@calendar.google.com", "accepted")
            });

        var json = JsonSerializer.Serialize(apiEvent, new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var client = CreateClientWithResponse("/calendars/org@calendar.google.com/events/evt-123", json);
        var result = await client.GetEventAsync("org@calendar.google.com", "evt-123", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("evt-123");
        result.OrganizerEmail.Should().Be("org@calendar.google.com");
        result.AttendeeEmails.Should().BeEquivalentTo(new[] { "att1@calendar.google.com", "att2@calendar.google.com" });
    }

    [Fact]
    public async Task GetEventAsync_ReturnsNullOn404()
    {
        var client = CreateClientWithStatusCode("/calendars/org@calendar.google.com/events/no-such", HttpStatusCode.NotFound);
        var result = await client.GetEventAsync("org@calendar.google.com", "no-such", CancellationToken.None);
        result.Should().BeNull();
    }

    // Factory helpers — omitted for brevity; use MockHttpMessageHandler pattern
    // or Microsoft.Extensions.Http.Testing / RichardSzalay.MockHttp
    private static GoogleCalendarClient CreateClientWithResponse(string path, string json)
        => throw new NotImplementedException("Replace with real HttpClient mock");
    private static GoogleCalendarClient CreateClientWithStatusCode(string path, HttpStatusCode status)
        => throw new NotImplementedException("Replace with real HttpClient mock");
}
```

**Note:** The test helpers above are stubs. Use the `MockHttpMessageHandler` pattern already present in the test project, or check how `CalendarsControllerTests` constructs its `HttpClient`. Replace the `throw` stubs with real implementations before running.

- [ ] **Step 4.1: Write and wire up the mapping tests (replace stubs with real HttpClient mocks)**

- [ ] **Step 4.2: Run to confirm failure**

```
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~GoogleCalendarClientMapping"
```

Expected: compile errors (interface methods don't exist yet).

### Step 4.3 — Update `IGoogleCalendarClient`

Add to `src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs`:

```csharp
/// <summary>
/// Full replacement of the attendees array. attendeeGoogleCalendarIds must contain ALL
/// calendars EXCEPT the organiser — Google keeps organiser and attendees as separate fields.
/// </summary>
Task PatchEventAttendeesAsync(
    string organizerCalendarId,
    string googleEventId,
    IEnumerable<string> attendeeGoogleCalendarIds,
    CancellationToken ct);

/// <summary>Returns null if the event is not found (404).</summary>
Task<GoogleEventDetail?> GetEventAsync(
    string googleCalendarId,
    string googleEventId,
    CancellationToken ct);
```

- [ ] **Step 4.3: Update `IGoogleCalendarClient`**

### Step 4.4 — Rewrite `GoogleCalendarClient`

Update `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs` to:

1. Replace all private response record classes (if any exist inline) with imports from `FamilyHQ.Services.Calendar.GoogleApi`.
2. Deserialise all HTTP responses into the `GoogleApi` records, then map to domain types.
3. Implement `PatchEventAttendeesAsync`:

```csharp
public async Task PatchEventAttendeesAsync(
    string organizerCalendarId,
    string googleEventId,
    IEnumerable<string> attendeeGoogleCalendarIds,
    CancellationToken ct = default)
{
    await SetAuthorizationHeaderAsync(ct);

    var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(organizerCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";

    var body = new
    {
        attendees = attendeeGoogleCalendarIds
            .Select(id => new { email = id })
            .ToArray()
    };

    var response = await _httpClient.PatchAsJsonAsync(endpoint, body, _jsonSerializerOptions, ct);
    response.EnsureSuccessStatusCode();
}
```

4. Implement `GetEventAsync`:

```csharp
public async Task<GoogleEventDetail?> GetEventAsync(
    string googleCalendarId,
    string googleEventId,
    CancellationToken ct = default)
{
    await SetAuthorizationHeaderAsync(ct);

    var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
    var response = await _httpClient.GetAsync(endpoint, ct);

    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return null;

    response.EnsureSuccessStatusCode();

    var apiEvent = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
    if (apiEvent is null) return null;

    return new GoogleEventDetail(
        Id: apiEvent.Id,
        OrganizerEmail: apiEvent.Organizer?.Email,
        AttendeeEmails: apiEvent.Attendees?.Select(a => a.Email).ToList() ?? []);
}
```

5. Update `GetCalendarsAsync` to use `GoogleApiCalendarList` instead of the inline `CalendarListResponse` type.
6. Update `GetEventsAsync` to use `GoogleApiEventList` and `GoogleApiEvent` instead of inline types.

- [ ] **Step 4.4: Rewrite `GoogleCalendarClient`**

- [ ] **Step 4.5: Run mapping tests**

```
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~GoogleCalendarClientMapping"
```

Expected: PASS.

- [ ] **Step 4.6: Run full services test suite**

```
dotnet test tests/FamilyHQ.Services.Tests
```

Expected: all pass.

- [ ] **Step 4.7: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/IGoogleCalendarClient.cs
src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs
src/FamilyHQ.Services/Calendar/GoogleApi/GoogleApiTypes.cs
tests/FamilyHQ.Services.Tests/Calendar/GoogleCalendarClientMappingTests.cs
git commit -m "feat(google-client): add PatchEventAttendeesAsync, GetEventAsync; migrate to internal GoogleApi types"
```

---

## Task 5 — DTO changes

**Files:**
- Modify: `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs`
- Modify: `src/FamilyHQ.Core/DTOs/UpdateEventRequest.cs`
- Modify: `src/FamilyHQ.Core/DTOs/CalendarEventDto.cs`
- Modify: `src/FamilyHQ.Core/DTOs/MonthViewDto.cs`

These are pure type changes. The compiler will surface all broken callers — fix them at the point you find them in later tasks, or fix them now to get a clean build.

### Step 5.1 — Update `CreateEventRequest`

```csharp
namespace FamilyHQ.Core.DTOs;

public record CreateEventRequest(
    IReadOnlyList<Guid> CalendarInfoIds,   // min 1, no duplicates; CalendarInfoIds[0] is the organiser
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
```

- [ ] **Step 5.1: Update `CreateEventRequest`**

### Step 5.2 — Update `UpdateEventRequest`

```csharp
namespace FamilyHQ.Core.DTOs;

public record UpdateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
```

- [ ] **Step 5.2: Update `UpdateEventRequest`**

### Step 5.3 — Update `CalendarEventDto`

```csharp
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
    IReadOnlyList<EventCalendarDto> Calendars);
```

Verify `EventCalendarDto` already exists: `record EventCalendarDto(Guid Id, string DisplayName, string? Color)`. If not, create it in `src/FamilyHQ.Core/DTOs/EventCalendarDto.cs`.

- [ ] **Step 5.3: Update `CalendarEventDto`**

### Step 5.4 — Fix `MonthViewDto`

```csharp
namespace FamilyHQ.Core.DTOs;

public class MonthViewDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public Dictionary<string, List<CalendarEventDto>> Days { get; set; } = new();
}
```

- [ ] **Step 5.4: Update `MonthViewDto`**

### Step 5.5 — Build to surface broken callers

```
dotnet build FamilyHQ.sln
```

Expected: compile errors in `CalendarsController`, `CalendarApiService`, tests. Note each error location — you will fix them in their respective tasks. Do not fix them here.

- [ ] **Step 5.5: Note all compile errors (do not fix yet)**

### Step 5.6 — Commit DTO changes

```bash
git add src/FamilyHQ.Core/DTOs/
git commit -m "feat(dtos): update CreateEventRequest, UpdateEventRequest, CalendarEventDto, MonthViewDto for multi-calendar"
```

- [ ] **Step 5.6: Commit**

---

## Task 6 — Validators

**Files:**
- Modify: `src/FamilyHQ.Core/Validators/CreateEventRequestValidator.cs`
- Create: `src/FamilyHQ.Core/Validators/UpdateEventRequestValidator.cs`
- Test: `tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs`
- Test: `tests/FamilyHQ.Core.Tests/Validators/UpdateEventRequestValidatorTests.cs`

### Step 6.1 — Write failing validator tests

**`CreateEventRequestValidator` tests** — add to (or create) `tests/FamilyHQ.Core.Tests/Validators/CreateEventRequestValidatorTests.cs`:

```csharp
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;

namespace FamilyHQ.Core.Tests.Validators;

public class CreateEventRequestValidatorTests
{
    private static readonly Guid CalA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static CreateEventRequest Valid(IReadOnlyList<Guid>? calIds = null) =>
        new(calIds ?? [CalA], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

    [Fact]
    public async Task Valid_SingleCalendar_Passes()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA]));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_MultipleCalendars_Passes()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA, CalB]));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Null_CalendarInfoIds_Fails()
    {
        var request = Valid() with { CalendarInfoIds = null! };
        var result = await new CreateEventRequestValidator().ValidateAsync(request);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_CalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_CalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([CalA, CalA]));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyGuid_InCalendarInfoIds_Fails()
    {
        var result = await new CreateEventRequestValidator().ValidateAsync(Valid([Guid.Empty]));
        result.IsValid.Should().BeFalse();
    }
}
```

Create `tests/FamilyHQ.Core.Tests/Validators/UpdateEventRequestValidatorTests.cs`:

```csharp
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Validators;

namespace FamilyHQ.Core.Tests.Validators;

public class UpdateEventRequestValidatorTests
{
    private static UpdateEventRequest Valid() =>
        new("Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_Title_Fails()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(Valid() with { Title = "" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Title_Over200Chars_Fails()
    {
        var result = await new UpdateEventRequestValidator().ValidateAsync(
            Valid() with { Title = new string('x', 201) });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task End_Before_Start_Fails()
    {
        var now = DateTimeOffset.UtcNow;
        var result = await new UpdateEventRequestValidator().ValidateAsync(
            new UpdateEventRequest("Title", now.AddHours(1), now, false, null, null));
        result.IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 6.1: Write validator tests**

- [ ] **Step 6.2: Run to confirm failure**

```
dotnet test tests/FamilyHQ.Core.Tests --filter "FullyQualifiedName~ValidatorTests"
```

Expected: compile errors / failures.

### Step 6.3 — Update `CreateEventRequestValidator`

```csharp
using FluentValidation;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Validators;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.CalendarInfoIds)
            .NotNull().WithMessage("At least one calendar is required.")
            .Must(ids => ids != null && ids.Count > 0).WithMessage("At least one calendar is required.")
            .Must(ids => ids == null || ids.All(id => id != Guid.Empty)).WithMessage("Calendar ID must not be empty.")
            .Must(ids => ids == null || ids.Distinct().Count() == ids.Count).WithMessage("Duplicate calendar IDs are not allowed.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Start).NotEmpty().WithMessage("Start time is required.");
        RuleFor(x => x.End).NotEmpty().WithMessage("End time is required.")
            .GreaterThanOrEqualTo(x => x.Start).WithMessage("End time must be after start time.");
    }
}
```

- [ ] **Step 6.3: Update `CreateEventRequestValidator`**

### Step 6.4 — Create `UpdateEventRequestValidator`

```csharp
using FluentValidation;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.Core.Validators;

public class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.").MaximumLength(200);
        RuleFor(x => x.Start).NotEmpty().WithMessage("Start time is required.");
        RuleFor(x => x.End).NotEmpty().WithMessage("End time is required.")
            .GreaterThanOrEqualTo(x => x.Start).WithMessage("End time must be after start time.");
    }
}
```

- [ ] **Step 6.4: Create `UpdateEventRequestValidator`**

- [ ] **Step 6.5: Run validator tests**

```
dotnet test tests/FamilyHQ.Core.Tests --filter "FullyQualifiedName~ValidatorTests"
```

Expected: all pass.

- [ ] **Step 6.6: Commit**

```bash
git add src/FamilyHQ.Core/Validators/
tests/FamilyHQ.Core.Tests/Validators/
git commit -m "feat(validators): update CreateEventRequestValidator for CalendarInfoIds list; add UpdateEventRequestValidator"
```

---

## Task 7 — `ICalendarEventService` + `CalendarEventService`

**Files:**
- Modify: `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs`
- Modify: `src/FamilyHQ.Services/Calendar/CalendarEventService.cs`
- Modify: `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs`

### Step 7.1 — Replace `ICalendarEventService`

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarEventService
{
    /// <summary>
    /// Creates the event in Google Calendar and persists to DB.
    /// CalendarInfoIds[0] becomes the Google organiser.
    /// Throws ValidationException if any CalendarInfoId is unknown to the user.
    /// </summary>
    Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates event fields (title, times, location, description) via the owner calendar.
    /// Throws NotFoundException if the event is missing or its owner is not in the user's calendar set.
    /// </summary>
    Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

    /// <summary>
    /// Adds targetCalendarInfoId to the event's calendar set.
    /// Idempotent: returns the event unchanged if already linked, without calling Google.
    /// Throws NotFoundException if targetCalendarInfoId is not in the user's calendar set.
    /// </summary>
    Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct = default);

    /// <summary>
    /// Removes calendarInfoId from the event's calendar set.
    /// If it is the last calendar, delegates to DeleteAsync.
    /// Throws NotFoundException if calendarInfoId is not linked to the event.
    /// </summary>
    Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the event. Performs live external-attendee check:
    /// skips Google delete if external parties are present; deletes Google event otherwise.
    /// Always deletes local rows.
    /// </summary>
    Task DeleteAsync(Guid eventId, CancellationToken ct = default);
}
```

- [ ] **Step 7.1: Replace `ICalendarEventService`**

### Step 7.2 — Write failing service tests

Replace `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs` with:

```csharp
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarEventServiceTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SingleCalendar_CallsCreateEventOnlyNoPatch()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "new-gid"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateAsync(request);

        google.Verify(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.GoogleEventId.Should().Be("new-gid");
        result.OwnerCalendarInfoId.Should().Be(CalAId);
    }

    [Fact]
    public async Task CreateAsync_TwoCalendars_CallsCreateThenPatch()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA, calB]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "new-gid"; return e; });
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var request = new CreateEventRequest(
            [CalAId, CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.CreateAsync(request);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "new-gid",
            It.Is<IEnumerable<string>>(ids => ids.Single() == "cal-b@google.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_UnknownCalendarId_ThrowsValidationException()
    {
        var (google, repo, sut) = CreateSut();
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Cal(CalAId, "cal-a@google.com")]);

        var request = new CreateEventRequest(
            [CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.Invoking(s => s.CreateAsync(request))
            .Should().ThrowAsync<Exception>(); // ValidationException or similar
    }

    [Fact]
    public async Task CreateAsync_DbFailureAfterGoogleSuccess_LogsReconciliationErrorAndRethrows()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "gid-db-fail"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        await sut.Invoking(s => s.CreateAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
        // Logger mock cannot be directly verified without setup — implementation is expected to log at Error level
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_CallsUpdateEventWithOwnerCalendar()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "old-gid", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateAsync(EventId, request);

        google.Verify(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Title.Should().Be("New Title");
    }

    // ── AddCalendarAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddCalendarAsync_CallsPatchWithNewAttendee()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // only calA linked
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.AddCalendarAsync(EventId, CalBId);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => ids.Contains("cal-b@google.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddCalendarAsync_Idempotent_NoGoogleCallWhenAlreadyLinked()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // calA already linked
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);

        await sut.AddCalendarAsync(EventId, CalAId);

        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RemoveCalendarAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RemoveCalendarAsync_NonOwner_CallsPatchWithoutRemovedCalendar()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA, calB); // owner=A, B is attendee
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.RemoveCalendarAsync(EventId, CalBId);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => !ids.Contains("cal-b@google.com")),
            It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.MoveEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveCalendarAsync_OwnerWithOthers_MovesEventThenPatchesInOrder()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA, calB); // owner=A, removing A
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);

        // Track call order explicitly — the spec requires Move THEN Patch, not the reverse
        var callOrder = new List<string>();
        google.Setup(g => g.MoveEventAsync("cal-a@google.com", "gid-1", "cal-b@google.com", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Move"))
            .ReturnsAsync("gid-1");
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Patch"))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.RemoveCalendarAsync(EventId, CalAId);

        google.Verify(g => g.MoveEventAsync("cal-a@google.com", "gid-1", "cal-b@google.com", It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-b@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => !ids.Any()), // empty — new owner has no other attendees
            It.IsAny<CancellationToken>()), Times.Once);
        callOrder.Should().ContainInOrder("Move", "Patch"); // Move must precede Patch
    }

    [Fact]
    public async Task RemoveCalendarAsync_LastCalendar_DelegatesToDelete()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // only calendar
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", []));
        google.Setup(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.RemoveCalendarAsync(EventId, CalAId);

        // PatchEventAttendeesAsync must NOT be called (delegated to DeleteAsync path)
        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        // MoveEventAsync must NOT be called — no remaining calendar to promote as new owner
        google.Verify(g => g.MoveEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NoExternalAttendees_CallsGoogleDelete()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", []));
        google.Setup(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ExternalAttendeePresent_SkipsGoogleDelete()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", ["external@gmail.com"]));
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_GetEventReturnsNull_SkipsGoogleDeleteDeletesLocally()
    {
        var (google, repo, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleEventDetail?)null);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = googleId };

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] cals) =>
        new()
        {
            Id = id,
            GoogleEventId = googleId,
            Title = "Test Event",
            Start = DateTimeOffset.UtcNow,
            End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = ownerCalId,
            Calendars = cals.ToList()
        };

    private static (Mock<IGoogleCalendarClient>, Mock<ICalendarRepository>, CalendarEventService) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var logger = new Mock<ILogger<CalendarEventService>>();
        return (google, repo, new CalendarEventService(google.Object, repo.Object, logger.Object));
    }
}
```

- [ ] **Step 7.2: Write service tests**

- [ ] **Step 7.3: Run to confirm failure**

```
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~CalendarEventServiceTests"
```

Expected: compile errors (service methods don't exist).

### Step 7.4 — Implement `CalendarEventService`

Replace `src/FamilyHQ.Services/Calendar/CalendarEventService.cs` with the full implementation. Key points:
- Constructor: `IGoogleCalendarClient googleCalendarClient, ICalendarRepository calendarRepository, ILogger<CalendarEventService> logger`
- Each method follows the exact sequence documented in the spec (see Service Behaviour section)
- `ValidationException` for unknown calendars in `CreateAsync` (use `FluentValidation.ValidationException` or throw `ArgumentException` — be consistent with what the test checks)
- Google API calls happen **before** opening DB transaction
- DB failure after Google success: log reconciliation error and rethrow

The full implementation is approximately 200 lines. Key method signatures:

```csharp
public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
public async Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct = default)
public async Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct = default)
public async Task DeleteAsync(Guid eventId, CancellationToken ct = default)
```

Refer to the spec section "Service Behaviour" for the exact logic of each method.

- [ ] **Step 7.4: Implement `CalendarEventService`**

- [ ] **Step 7.5: Run service tests**

```
dotnet test tests/FamilyHQ.Services.Tests
```

Expected: all pass.

- [ ] **Step 7.6: Commit**

```bash
git add src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs
src/FamilyHQ.Services/Calendar/CalendarEventService.cs
tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs
git commit -m "feat(service): replace ReassignAsync with CreateAsync/UpdateAsync/AddCalendarAsync/RemoveCalendarAsync/DeleteAsync"
```

---

## Task 8 — `CalendarSyncService` update

**Files:**
- Modify: `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs`

`CalendarSyncService.SyncAsync` currently constructs `CalendarEvent` objects without `OwnerCalendarInfoId` or `IsExternallyOwned`. It also uses a second-calendar attach path (lines ~150–175). Both paths need updating.

- [ ] **Step 8.1: Find existing sync service tests**

```
ls tests/FamilyHQ.Services.Tests/Calendar/
```

- [ ] **Step 8.1a: Write failing assertions for new sync fields (TDD — write before implementation)**

In the existing sync test file (or a new `CalendarSyncServiceOwnerTests.cs`), add:

```csharp
private static readonly Guid CalAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
private static readonly Guid CalBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

[Fact]
public async Task SyncAsync_FirstSync_SetsOwnerCalendarInfoId()
{
    // Arrange
    var google = new Mock<IGoogleCalendarClient>();
    var repo   = new Mock<ICalendarRepository>();
    var logger = new Mock<ILogger<CalendarSyncService>>();
    var sut    = new CalendarSyncService(google.Object, repo.Object, logger.Object);

    var calInfo = new CalendarInfo { Id = CalAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
    repo.Setup(r => r.GetCalendarByIdAsync(CalAId, It.IsAny<CancellationToken>())).ReturnsAsync(calInfo);
    repo.Setup(r => r.GetSyncStateAsync(CalAId, It.IsAny<CancellationToken>())).ReturnsAsync((SyncState?)null);

    var googleEvent = new CalendarEvent
    {
        GoogleEventId = "gid-1",
        Title = "Meeting",
        Start = DateTimeOffset.UtcNow,
        End   = DateTimeOffset.UtcNow.AddHours(1),
        IsAllDay = false,
        IsExternallyOwned = false   // organizer.Self = true maps to IsExternallyOwned = false
    };
    google.Setup(g => g.GetEventsAsync("cal-a@google.com",
        It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync((new[] { googleEvent }, "sync-token"));

    repo.Setup(r => r.GetEventsAsync(CalAId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);
    repo.Setup(r => r.GetEventByGoogleEventIdAsync("gid-1", It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

    CalendarEvent? savedEvent = null;
    repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
        .Callback<CalendarEvent, CancellationToken>((e, _) => savedEvent = e)
        .Returns(Task.CompletedTask);
    repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    // Act
    await sut.SyncAsync(CalAId, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));

    // Assert
    savedEvent.Should().NotBeNull();
    savedEvent!.OwnerCalendarInfoId.Should().Be(CalAId);
}

[Fact]
public async Task SyncAsync_OrganizerSelfFalse_SetsIsExternallyOwned()
{
    // Arrange
    var google = new Mock<IGoogleCalendarClient>();
    var repo   = new Mock<ICalendarRepository>();
    var logger = new Mock<ILogger<CalendarSyncService>>();
    var sut    = new CalendarSyncService(google.Object, repo.Object, logger.Object);

    var calInfo = new CalendarInfo { Id = CalAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
    repo.Setup(r => r.GetCalendarByIdAsync(CalAId, It.IsAny<CancellationToken>())).ReturnsAsync(calInfo);
    repo.Setup(r => r.GetSyncStateAsync(CalAId, It.IsAny<CancellationToken>())).ReturnsAsync((SyncState?)null);

    // IsExternallyOwned = true because organizer.Self = false (mapped by GetEventsAsync in GoogleCalendarClient)
    var googleEvent = new CalendarEvent
    {
        GoogleEventId = "gid-ext",
        Title = "External Meeting",
        Start = DateTimeOffset.UtcNow,
        End   = DateTimeOffset.UtcNow.AddHours(1),
        IsAllDay = false,
        IsExternallyOwned = true   // set by GoogleCalendarClient when organizer.Self = false
    };
    google.Setup(g => g.GetEventsAsync("cal-a@google.com",
        It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync((new[] { googleEvent }, "sync-token"));

    repo.Setup(r => r.GetEventsAsync(CalAId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);
    repo.Setup(r => r.GetEventByGoogleEventIdAsync("gid-ext", It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

    CalendarEvent? savedEvent = null;
    repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
        .Callback<CalendarEvent, CancellationToken>((e, _) => savedEvent = e)
        .Returns(Task.CompletedTask);
    repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    // Act
    await sut.SyncAsync(CalAId, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));

    // Assert
    savedEvent.Should().NotBeNull();
    savedEvent!.IsExternallyOwned.Should().BeTrue();
}

[Fact]
public async Task SyncAsync_SecondCalendarAttach_DoesNotOverwriteOwnerCalendarInfoId()
{
    // Arrange — event already exists linked to CalA (OwnerCalendarInfoId = CalAId)
    //           SyncAsync now runs for CalB (as attendee)
    var google = new Mock<IGoogleCalendarClient>();
    var repo   = new Mock<ICalendarRepository>();
    var logger = new Mock<ILogger<CalendarSyncService>>();
    var sut    = new CalendarSyncService(google.Object, repo.Object, logger.Object);

    var calInfoB = new CalendarInfo { Id = CalBId, GoogleCalendarId = "cal-b@google.com", DisplayName = "Cal B" };
    repo.Setup(r => r.GetCalendarByIdAsync(CalBId, It.IsAny<CancellationToken>())).ReturnsAsync(calInfoB);
    repo.Setup(r => r.GetSyncStateAsync(CalBId, It.IsAny<CancellationToken>())).ReturnsAsync((SyncState?)null);

    var googleEvent = new CalendarEvent
    {
        GoogleEventId = "gid-1",
        Title = "Meeting",
        Start = DateTimeOffset.UtcNow,
        End   = DateTimeOffset.UtcNow.AddHours(1),
        IsExternallyOwned = false
    };
    google.Setup(g => g.GetEventsAsync("cal-b@google.com",
        It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync((new[] { googleEvent }, "sync-token-b"));

    repo.Setup(r => r.GetEventsAsync(CalBId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([]);

    // The event already exists in DB with OwnerCalendarInfoId = CalAId
    var existingEvent = new CalendarEvent
    {
        Id = Guid.NewGuid(),
        GoogleEventId = "gid-1",
        OwnerCalendarInfoId = CalAId,  // set by CalA's earlier sync
        Calendars = new List<CalendarInfo>()
    };
    repo.Setup(r => r.GetEventByGoogleEventIdAsync("gid-1", It.IsAny<CancellationToken>())).ReturnsAsync(existingEvent);
    repo.Setup(r => r.GetEventByIdAsync(existingEvent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existingEvent);

    CalendarEvent? updatedEvent = null;
    repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
        .Callback<CalendarEvent, CancellationToken>((e, _) => updatedEvent = e)
        .Returns(Task.CompletedTask);
    repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    // Act
    await sut.SyncAsync(CalBId, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));

    // Assert — OwnerCalendarInfoId must still be CalAId, not overwritten with CalBId
    updatedEvent.Should().NotBeNull();
    updatedEvent!.OwnerCalendarInfoId.Should().Be(CalAId);
    updatedEvent.Calendars.Should().Contain(c => c.Id == CalBId);
}
```

Run to confirm failure:
```
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~SyncService"
```

Expected: compile errors or assertion failures.

- [ ] **Step 8.2: Update `SyncAsync` — first-calendar path**

In the block where a new event is created (no existing DB entry for this `GoogleEventId`):

```csharp
var newEvent = new CalendarEvent
{
    GoogleEventId = evt.GoogleEventId,
    Title = evt.Title,
    Start = evt.Start,
    End = evt.End,
    IsAllDay = evt.IsAllDay,
    Location = evt.Location,
    Description = evt.Description,
    OwnerCalendarInfoId = calendar.Id,       // set to the currently synced calendar
    IsExternallyOwned = evt.IsExternallyOwned // from GetEventsAsync mapping (see below)
};
newEvent.Calendars.Add(calendar);
await _calendarRepository.AddEventAsync(newEvent, ct);
```

- [ ] **Step 8.3: Update `GetEventsAsync` mapping in `GoogleCalendarClient` to populate `IsExternallyOwned`**

In `GetEventsAsync`, when constructing `CalendarEvent` from `GoogleApiEvent`:

```csharp
events.Add(new CalendarEvent
{
    GoogleEventId = item.Id,
    Title = item.Summary ?? "Untitled Event",
    Start = startParam.Value,
    End = endParam.Value,
    IsAllDay = isAllDay,
    Location = item.Location,
    Description = item.Description,
    IsExternallyOwned = item.Organizer?.Self == false
    // OwnerCalendarInfoId is set by SyncAsync, not here
});
```

- [ ] **Step 8.4: Update second-calendar attach path**

In the "event already exists in DB, add this calendar" branch — do NOT change `OwnerCalendarInfoId`:

```csharp
// Second-calendar attach: add this calendar to existing event's Calendars.
// Do NOT change OwnerCalendarInfoId — owner was established at first sync.
if (tracked != null && !tracked.Calendars.Any(c => c.Id == calendarInfoId))
{
    tracked.Calendars.Add(calendar);
    await _calendarRepository.UpdateEventAsync(tracked, ct);
}
```

- [ ] **Step 8.5: Run services tests**

```
dotnet test tests/FamilyHQ.Services.Tests
```

Expected: all pass.

- [ ] **Step 8.6: Commit**

```bash
git add src/FamilyHQ.Services/Calendar/CalendarSyncService.cs
src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs
git commit -m "feat(sync): populate OwnerCalendarInfoId and IsExternallyOwned during sync; guard second-calendar attach"
```

---

## Task 9 — Controller changes

**Files:**
- Modify: `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs`
- Create: `src/FamilyHQ.WebApi/Controllers/EventsController.cs`
- Modify: `tests/FamilyHQ.WebApi.Tests/Controllers/CalendarsControllerTests.cs`
- Create: `tests/FamilyHQ.WebApi.Tests/Controllers/EventsControllerTests.cs`

### Step 9.1 — Write failing EventsController tests

Create `tests/FamilyHQ.WebApi.Tests/Controllers/EventsControllerTests.cs`:

```csharp
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class EventsControllerTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── POST /api/events ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEvent_Returns201WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var newEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.CreateAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newEvent);

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateEvent(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = created.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        dto.GoogleEventId.Should().Be("gid-1");
        dto.Calendars.Should().ContainSingle(c => c.Id == CalAId);
        // No ViewModel fields in response
        dto.Should().BeOfType<CalendarEventDto>();
    }

    [Fact]
    public async Task CreateEvent_InvalidRequest_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new CreateEventRequest([], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateEvent(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── PUT /api/events/{eventId} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateEvent_Returns200WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.UpdateAsync(EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new UpdateEventRequest(
            "New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateEvent(EventId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        dto.Calendars.Should().ContainSingle(c => c.Id == CalAId);
    }

    [Fact]
    public async Task UpdateEvent_InvalidRequest_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new UpdateEventRequest(
            "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateEvent(EventId, request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DELETE /api/events/{eventId} ──────────────────────────────────────────

    [Fact]
    public async Task DeleteEvent_Returns204()
    {
        var (service, sut) = CreateSut();
        service.Setup(s => s.DeleteAsync(EventId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.DeleteEvent(EventId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    // ── POST /api/events/{eventId}/calendars/{calendarId} ────────────────────

    [Fact]
    public async Task AddCalendar_Returns200WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.AddCalendarAsync(EventId, CalAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var result = await sut.AddCalendar(EventId, CalAId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<CalendarEventDto>();
    }

    // ── DELETE /api/events/{eventId}/calendars/{calendarId} ──────────────────

    [Fact]
    public async Task RemoveCalendar_Returns204()
    {
        var (service, sut) = CreateSut();
        service.Setup(s => s.RemoveCalendarAsync(EventId, CalAId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.RemoveCalendar(EventId, CalAId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = "Cal" };

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] cals) =>
        new() { Id = id, GoogleEventId = googleId, Title = "Test",
                Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
                OwnerCalendarInfoId = ownerCalId, Calendars = cals.ToList() };

    private static (Mock<ICalendarEventService>, EventsController) CreateSut()
    {
        var service = new Mock<ICalendarEventService>();
        var logger  = new Mock<ILogger<EventsController>>();
        return (service, new EventsController(service.Object, logger.Object));
    }
}
```

- [ ] **Step 9.1: Write EventsController tests**

- [ ] **Step 9.2: Run to confirm failure**

```
dotnet test tests/FamilyHQ.WebApi.Tests --filter "FullyQualifiedName~EventsControllerTests"
```

Expected: compile error — `EventsController` doesn't exist yet.

### Step 9.3 — Create `EventsController`

Create `src/FamilyHQ.WebApi/Controllers/EventsController.cs`:

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
        var validator = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var created = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(CreateEvent), new { eventId = created.Id }, MapToDto(created));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator = new Core.Validators.UpdateEventRequestValidator();
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

    [HttpPost("{eventId:guid}/calendars/{calendarId:guid}")]
    public async Task<IActionResult> AddCalendar(Guid eventId, Guid calendarId, CancellationToken ct)
    {
        var updated = await _service.AddCalendarAsync(eventId, calendarId, ct);
        return Ok(MapToDto(updated));
    }

    [HttpDelete("{eventId:guid}/calendars/{calendarId:guid}")]
    public async Task<IActionResult> RemoveCalendar(Guid eventId, Guid calendarId, CancellationToken ct)
    {
        await _service.RemoveCalendarAsync(eventId, calendarId, ct);
        return NoContent();
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
        e.Calendars
            .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
            .ToList());
}
```

- [ ] **Step 9.3: Create `EventsController`**

### Step 9.4 — Clean up `CalendarsController`

Modify `src/FamilyHQ.WebApi/Controllers/CalendarsController.cs`:
1. Remove `CreateEvent`, `UpdateEvent`, `DeleteEvent`, `ReassignEvent` methods (and their route attributes).
2. Remove `ICalendarEventService` and `IGoogleCalendarClient` constructor params (no longer needed).
3. Fix `GetEventsForMonth` — replace the ViewModel construction with `CalendarEventDto` construction:

```csharp
var monthView = new MonthViewDto { Year = year, Month = month };

foreach (var evt in events)
{
    var dto = new CalendarEventDto(
        evt.Id,
        evt.GoogleEventId,
        evt.Title,
        evt.Start,
        evt.End,
        evt.IsAllDay,
        evt.Location,
        evt.Description,
        evt.Calendars.Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color)).ToList());

    var dateKey = evt.Start.ToString("yyyy-MM-dd");
    if (!monthView.Days.ContainsKey(dateKey))
        monthView.Days[dateKey] = [];

    monthView.Days[dateKey].Add(dto);
}

return Ok(monthView);
```

4. Fix `GetCalendars` — map to `EventCalendarDto` (or a simple anonymous type) instead of returning raw `CalendarInfo` domain models:

```csharp
[HttpGet]
public async Task<IActionResult> GetCalendars(CancellationToken ct)
{
    var calendars = await _calendarRepository.GetCalendarsAsync(ct);
    var dtos = calendars.Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color));
    return Ok(dtos);
}
```

- [ ] **Step 9.4: Clean up `CalendarsController`**

### Step 9.5 — Update `CalendarsControllerTests`

Remove tests for `CreateEvent`, `UpdateEvent`, `DeleteEvent`, `ReassignEvent` (they move to the new controller). Update `GetEventsForMonth` test to expect `CalendarEventDto` in `Days` instead of `CalendarEventViewModel`. Verify `GetCalendars` returns DTOs not domain models.

- [ ] **Step 9.5: Update `CalendarsControllerTests`**

- [ ] **Step 9.6: Run WebApi tests**

```
dotnet test tests/FamilyHQ.WebApi.Tests
```

Expected: all pass.

- [ ] **Step 9.7: Commit**

```bash
git add src/FamilyHQ.WebApi/Controllers/
tests/FamilyHQ.WebApi.Tests/Controllers/
git commit -m "feat(api): add EventsController with 5 event-centric endpoints; clean up CalendarsController"
```

---

## Task 10 — WebUi ViewModels

**Files:**
- Create: `src/FamilyHQ.WebUi/ViewModels/CalendarSummaryViewModel.cs`
- Create: `src/FamilyHQ.WebUi/ViewModels/MonthViewModel.cs`
- Create: `src/FamilyHQ.WebUi/ViewModels/CalendarEventViewModel.cs`
- Delete: `src/FamilyHQ.Core/ViewModels/CalendarEventViewModel.cs`

### Step 10.1 — Create `CalendarSummaryViewModel`

```csharp
namespace FamilyHQ.WebUi.ViewModels;

public record CalendarSummaryViewModel(Guid Id, string DisplayName, string? Color);
```

- [ ] **Step 10.1: Create `CalendarSummaryViewModel`**

### Step 10.2 — Create `CalendarEventViewModel`

```csharp
namespace FamilyHQ.WebUi.ViewModels;

public record CalendarEventViewModel(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    // The calendar this capsule represents on the grid
    Guid CalendarInfoId,
    string CalendarDisplayName,
    string? CalendarColor,
    // All calendars this event belongs to — for chip rendering in edit modal
    IReadOnlyList<CalendarSummaryViewModel> AllCalendars);
```

- [ ] **Step 10.2: Create `CalendarEventViewModel`**

### Step 10.3 — Create `MonthViewModel`

```csharp
namespace FamilyHQ.WebUi.ViewModels;

public record MonthViewModel(Dictionary<string, List<CalendarEventViewModel>> Days);
```

- [ ] **Step 10.3: Create `MonthViewModel`**

### Step 10.4 — Delete old `CalendarEventViewModel`

Delete `src/FamilyHQ.Core/ViewModels/CalendarEventViewModel.cs`. If `FamilyHQ.Core/ViewModels/` becomes empty, delete the folder too. Update all `using FamilyHQ.Core.ViewModels;` references in WebUi and WebApi to `using FamilyHQ.WebUi.ViewModels;`.

- [ ] **Step 10.4: Delete Core ViewModel; update all usages**

- [ ] **Step 10.5: Build WebUi project**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: success or only errors in `CalendarApiService` (which you'll fix in Task 11).

- [ ] **Step 10.6: Commit**

```bash
git add src/FamilyHQ.WebUi/ViewModels/
git rm src/FamilyHQ.Core/ViewModels/CalendarEventViewModel.cs
git commit -m "feat(viewmodels): move CalendarEventViewModel to WebUi; add CalendarSummaryViewModel, MonthViewModel"
```

---

## Task 11 — `ICalendarApiService` + `CalendarApiService`

**Files:**
- Modify: `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/CalendarApiService.cs`
- Test: `tests/FamilyHQ.WebUi.Tests/Services/CalendarApiServiceTests.cs`

**Prerequisite:** Task 5 must be complete (`MonthViewDto.Days` must be `Dictionary<string, List<CalendarEventDto>>`). The tests below will not compile against the old shape.

### Step 11.1 — Write failing CalendarApiService tests

Create / update `tests/FamilyHQ.WebUi.Tests/Services/CalendarApiServiceTests.cs`:

```csharp
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.ViewModels;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FamilyHQ.WebUi.Tests.Services;

public class CalendarApiServiceTests
{
    [Fact]
    public async Task GetEventsForMonthAsync_ExpandsMultiCalendarEventIntoTwoViewModels()
    {
        // One CalendarEventDto with 2 calendars → 2 CalendarEventViewModels in Days
        var calA = new EventCalendarDto(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Cal A", "#ff0000");
        var calB = new EventCalendarDto(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Cal B", "#0000ff");

        var today = DateTimeOffset.UtcNow.Date.ToString("yyyy-MM-dd");
        var dto = new CalendarEventDto(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "gid-1", "Team Meeting",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, null,
            [calA, calB]);

        var monthViewDto = new MonthViewDto
        {
            Year = DateTime.UtcNow.Year,
            Month = DateTime.UtcNow.Month,
            Days = new() { [today] = [dto] }
        };

        var httpClient = CreateFakeClient("/api/calendars/events", monthViewDto);
        var sut = new CalendarApiService(httpClient);

        var result = await sut.GetEventsForMonthAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month, CancellationToken.None);

        result.Days.Should().ContainKey(today);
        var vms = result.Days[today];
        vms.Should().HaveCount(2); // one per calendar
        vms.Should().Contain(v => v.CalendarInfoId == calA.Id && v.CalendarColor == "#ff0000");
        vms.Should().Contain(v => v.CalendarInfoId == calB.Id && v.CalendarColor == "#0000ff");
        // AllCalendars populated on both
        vms.Should().AllSatisfy(v => v.AllCalendars.Should().HaveCount(2));
    }

    [Fact]
    public async Task GetEventsForMonthAsync_NoDtoTypeInViewModels()
    {
        // Regression: no EventCalendarDto must appear in the returned ViewModels
        var calA = new EventCalendarDto(Guid.NewGuid(), "Cal A", "#ff0000");
        var today = DateTimeOffset.UtcNow.Date.ToString("yyyy-MM-dd");
        var dto = new CalendarEventDto(Guid.NewGuid(), "gid-1", "Event",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null, [calA]);

        var monthViewDto = new MonthViewDto
        {
            Year = DateTime.UtcNow.Year, Month = DateTime.UtcNow.Month,
            Days = new() { [today] = [dto] }
        };

        var httpClient = CreateFakeClient("/api/calendars/events", monthViewDto);
        var sut = new CalendarApiService(httpClient);

        var result = await sut.GetEventsForMonthAsync(DateTime.UtcNow.Year, DateTime.UtcNow.Month, CancellationToken.None);

        // AllCalendars must be IReadOnlyList<CalendarSummaryViewModel>, not EventCalendarDto
        var vm = result.Days[today].Single();
        vm.AllCalendars.Should().AllBeOfType<CalendarSummaryViewModel>();
    }

    // Stub — replace with MockHttpMessageHandler pattern used in the project
    private static HttpClient CreateFakeClient<T>(string path, T response) =>
        throw new NotImplementedException("Replace with real HttpClient mock");
}
```

- [ ] **Step 11.1: Write and wire up CalendarApiService tests**

- [ ] **Step 11.2: Run to confirm failure**

```
dotnet test tests/FamilyHQ.WebUi.Tests --filter "FullyQualifiedName~CalendarApiServiceTests"
```

### Step 11.3 — Rewrite `ICalendarApiService`

```csharp
using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Services;

public interface ICalendarApiService
{
    Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct = default);
    Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default);
    Task<CalendarEventViewModel> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default);
    Task<CalendarEventViewModel> UpdateEventAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
    Task DeleteEventAsync(Guid eventId, CancellationToken ct = default);
    Task<CalendarEventViewModel> AddCalendarToEventAsync(Guid eventId, Guid calendarId, CancellationToken ct = default);
    Task RemoveCalendarFromEventAsync(Guid eventId, Guid calendarId, CancellationToken ct = default);
}
```

- [ ] **Step 11.3: Rewrite `ICalendarApiService`**

### Step 11.4 — Rewrite `CalendarApiService`

Key mapping logic for `GetEventsForMonthAsync`:

```csharp
public async Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default)
{
    var response = await _httpClient.GetAsync($"api/calendars/events?year={year}&month={month}", ct);
    response.EnsureSuccessStatusCode();
    var dto = await response.Content.ReadFromJsonAsync<MonthViewDto>(cancellationToken: ct)
              ?? new MonthViewDto();

    var expandedDays = new Dictionary<string, List<CalendarEventViewModel>>();
    foreach (var (dateKey, eventDtos) in dto.Days)
    {
        var vms = new List<CalendarEventViewModel>();
        foreach (var evtDto in eventDtos)
        {
            var allCalendars = evtDto.Calendars
                .Select(c => new CalendarSummaryViewModel(c.Id, c.DisplayName, c.Color))
                .ToList();

            // One ViewModel per calendar — grid expansion happens here
            foreach (var cal in evtDto.Calendars)
            {
                vms.Add(new CalendarEventViewModel(
                    evtDto.Id, evtDto.Title, evtDto.Start, evtDto.End, evtDto.IsAllDay,
                    evtDto.Location, evtDto.Description,
                    cal.Id, cal.DisplayName, cal.Color,
                    allCalendars));
            }
        }
        expandedDays[dateKey] = vms;
    }
    return new MonthViewModel(expandedDays);
}
```

`GetCalendarsAsync` maps `EventCalendarDto` → `CalendarSummaryViewModel` (the API returns `EventCalendarDto` from `CalendarsController.GetCalendars`).

`CreateEventAsync` and `UpdateEventAsync` post to `/api/events` and `/api/events/{eventId}`, deserialise `CalendarEventDto`, and map to a single `CalendarEventViewModel` (using the first calendar in `Calendars` as the `CalendarInfoId` for the returned capsule).

- [ ] **Step 11.4: Rewrite `CalendarApiService`**

- [ ] **Step 11.5: Run WebUi tests**

```
dotnet test tests/FamilyHQ.WebUi.Tests
```

Expected: all pass.

- [ ] **Step 11.6: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/
tests/FamilyHQ.WebUi.Tests/Services/
git commit -m "feat(ui-service): rewrite ICalendarApiService and CalendarApiService for multi-calendar ViewModels"
```

---

## Task 12 — UI chip selector

**Files:**
- Modify: the Blazor component(s) that render the create/edit event modal

Before starting, run:
```
ls src/FamilyHQ.WebUi/Components/
```
to identify the modal component (e.g. `EventModal.razor`, `CreateEventModal.razor`, or similar).

Read `.agent/skills/frontend-design/SKILL.md` before making any UI changes.

### Step 12.1 — Find the event create/edit modal component

```
grep -r "CreateEvent\|UpdateEvent\|ReassignEvent\|calendarId" src/FamilyHQ.WebUi/Components/ --include="*.razor" -l
```

- [ ] **Step 12.1: Identify modal component(s)**

### Step 12.2 — Update modal to use chip selector

In the modal component:

1. Inject `ICalendarApiService` and call `GetCalendarsAsync()` on init to populate `_allCalendars` (`IReadOnlyList<CalendarSummaryViewModel>`).

2. **Create mode**: Replace the single calendar dropdown with a chip list. Each chip is a `CalendarSummaryViewModel`. Track `_selectedCalendarIds` as `HashSet<Guid>`. On save, build `CreateEventRequest` with `CalendarInfoIds = _selectedCalendarIds.ToList()`.

3. **Edit mode**: On open, populate `_selectedCalendarIds` from `CalendarEventViewModel.AllCalendars`.
   - Click inactive chip → call `ICalendarApiService.AddCalendarToEventAsync(eventId, chip.Id)`.
   - Click active chip → if more than one active, call `ICalendarApiService.RemoveCalendarFromEventAsync(eventId, chip.Id)`.
   - Last active chip has no ✕ and is visually distinct. Tooltip: "Use Delete to remove this event entirely."

4. Delete button calls `ICalendarApiService.DeleteEventAsync(eventId)`.

Chip rendering example:

```razor
@foreach (var cal in _allCalendars)
{
    var isActive = _selectedCalendarIds.Contains(cal.Id);
    var isLast   = isActive && _selectedCalendarIds.Count == 1;
    <div class="chip @(isActive ? "chip-active" : "chip-inactive")"
         style="--chip-color: @cal.Color"
         @onclick="() => ToggleCalendar(cal)"
         title="@(isLast ? "Use Delete to remove this event entirely." : "")">
        <span class="chip-dot"></span>
        <span class="chip-name">@cal.DisplayName</span>
        @if (isActive && !isLast)
        {
            <span class="chip-remove">✕</span>
        }
    </div>
}
```

- [ ] **Step 12.2: Implement chip selector in modal**

- [ ] **Step 12.3: Build WebUi**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: success.

- [ ] **Step 12.4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/
git commit -m "feat(ui): replace calendar dropdown with chip selector in event modal"
```

---

## Task 13 — E2E acceptance tests

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature`
- Modify or create step definitions in `tests-e2e/FamilyHQ.E2E.Steps/` (check existing step files)

Read `.agent/skills/bdd-testing/SKILL.md` before writing these tests.

### Step 13.1 — Read existing step definitions

```
ls tests-e2e/FamilyHQ.E2E.Steps/
```

- [ ] **Step 13.1: Read existing step definitions**

### Step 13.2 — Add multi-calendar scenarios to `Dashboard.feature`

Add to `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature`:

```gherkin
  Scenario: Create event in two calendars appears twice on grid
    Given I have a user like "MultiCalUser" with calendar "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I create an event "Standup" in calendars "Work Calendar" and "Personal Calendar"
    Then I see the event "Standup" displayed on the calendar in "Work Calendar" colour
    And I see the event "Standup" displayed on the calendar in "Personal Calendar" colour

  Scenario: Add calendar to existing event via chip
    Given I have a user like "MultiCalUser" with calendar "Work Calendar"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I open the event "Team Meeting" for editing
    And I add the calendar "Personal Calendar" chip to the event
    Then I see the event "Team Meeting" displayed on the calendar in "Work Calendar" colour
    And I see the event "Team Meeting" displayed on the calendar in "Personal Calendar" colour

  Scenario: Remove calendar chip from event
    Given I have a user like "MultiCalUser" with calendar "Work Calendar"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I open the event "Team Meeting" for editing
    And I remove the calendar "Personal Calendar" chip from the event
    Then I see the event "Team Meeting" displayed on the calendar in "Work Calendar" colour
    And I do not see a "Personal Calendar" capsule for "Team Meeting" on the calendar

  Scenario: Last chip is protected — cannot remove final calendar
    Given I have a user like "MultiCalUser" with calendar "Work Calendar"
    And the user has an all-day event "Solo Event" tomorrow in "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I open the event "Solo Event" for editing
    Then the last active calendar chip has no remove button

  Scenario: Delete event removes it from all calendars
    Given I have a user like "MultiCalUser" with calendar "Work Calendar"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I delete the event "Team Meeting"
    Then I do not see the event "Team Meeting" displayed on the calendar
```

- [ ] **Step 13.2: Add multi-calendar scenarios**

### Step 13.3 — Implement missing step definitions

For any new `Given/When/Then` steps not yet defined, add them to the relevant step definition file. Refer to `.agent/docs/e2e-testing-maintenance.md` for the project's BDD test patterns.

- [ ] **Step 13.3: Implement step definitions**

- [ ] **Step 13.4: Commit**

```bash
git add tests-e2e/
git commit -m "test(e2e): add multi-calendar BDD acceptance scenarios"
```

---

## Final verification

Before marking implementation complete, run all unit tests end-to-end:

```
dotnet test tests/FamilyHQ.Core.Tests
dotnet test tests/FamilyHQ.Services.Tests
dotnet test tests/FamilyHQ.WebApi.Tests
dotnet test tests/FamilyHQ.WebUi.Tests
dotnet test tests/FamilyHQ.Simulator.Tests
dotnet build FamilyHQ.sln
```

All must report 0 failures before proceeding to E2E or creating a PR.

---

## Reference: spec document

`docs/superpowers/specs/2026-03-21-event-multi-calendars-design.md`
