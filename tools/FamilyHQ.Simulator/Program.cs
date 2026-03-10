using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<SimContext>(options =>
    options.UseInMemoryDatabase("SimulatorDb"));

var app = builder.Build();

// Seed initial data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SimContext>();
    SeedData(db);
}

// Dummy OAuth Token Exchange / Refresh
app.MapPost("/token", async context =>
{
    var form = await context.Request.ReadFormAsync();
    var code = form["code"].ToString();
    
    var userId = "default_simulator_user";
    if (code.StartsWith("dummy_code_for_"))
    {
        userId = code.Substring("dummy_code_for_".Length);
    }
    
    var response = new
    {
        access_token = "simulated_access_token_" + Guid.NewGuid().ToString("N"),
        refresh_token = "simulated_refresh_token",
        expires_in = 3600,
        token_type = "Bearer",
        user_id = userId
    };
    
    await context.Response.WriteAsJsonAsync(response);
});

// Dummy Calendar List
app.MapGet("/users/me/calendarList", async context =>
{
    var response = new
    {
        items = FamilyHQ.Simulator.Program.ActiveCalendars
    };
    
    await context.Response.WriteAsJsonAsync(response);
});

// Configure Endpoint for E2E tests
app.MapPost("/api/simulator/configure", async (HttpRequest request, SimContext db) =>
{
    var config = await request.ReadFromJsonAsync<SimulatorConfigurationModel>();
    if (config != null)
    {
        // Update Calendars
        FamilyHQ.Simulator.Program.ActiveCalendars.Clear();
        foreach (var c in config.Calendars)
        {
            FamilyHQ.Simulator.Program.ActiveCalendars.Add(new { id = c.Id, summary = c.Summary, backgroundColor = c.BackgroundColor ?? "#9e9e9e" });
        }

        // Clear and seed Events
        var existingEvents = await db.Events.ToListAsync();
        db.Events.RemoveRange(existingEvents);
        
        foreach (var e in config.Events)
        {
            db.Events.Add(new SimulatedEvent
            {
                Id = e.Id,
                CalendarId = e.CalendarId,
                Summary = e.Summary,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                IsAllDay = e.IsAllDay
            });
        }
        
        await db.SaveChangesAsync();
        Console.WriteLine($"[SIM] Configured Simulator with {config.Calendars.Count} calendars and {config.Events.Count} events from template.");
    }
    
    return Results.Ok();
});

// Persistent Events List
app.MapGet("/calendars/{calendarId}/events", async (string calendarId, SimContext db) =>
{
    Console.WriteLine($"[SIM] GET events for calendar: {calendarId}");
    var events = await db.Events
        .Where(e => e.CalendarId == calendarId)
        .ToListAsync();

    var response = new
    {
        items = events.Select(e => new
        {
            id = e.Id,
            status = "confirmed",
            summary = e.Summary,
            location = e.Location,
            description = e.Description,
            start = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
            end = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = e.EndTime.ToString("O") }
        }),
        nextSyncToken = "simulated_sync_token_" + Guid.NewGuid().ToString("N")
    };
    
    return Results.Ok(response);
});

// Persistent Create Event
app.MapPost("/calendars/{calendarId}/events", async (string calendarId, HttpRequest request, SimContext db) =>
{
    Console.WriteLine($"[SIM] POST create event for calendar: {calendarId}");
    var body = await request.ReadFromJsonAsync<GoogleEventRequest>();
    if (body == null) 
    {
        Console.WriteLine("[SIM] Error: Failed to deserialize request body.");
        return Results.BadRequest();
    }

    var newEvent = new SimulatedEvent
    {
        Id = "simulated_evt_" + Guid.NewGuid().ToString("N"),
        CalendarId = calendarId,
        Summary = body.Summary ?? "New Event",
        Location = body.Location,
        Description = body.Description,
        StartTime = body.Start.DateTime ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date) : DateTime.UtcNow),
        EndTime = body.End.DateTime ?? (body.End.Date != null ? DateTime.Parse(body.End.Date) : DateTime.UtcNow.AddHours(1)),
        IsAllDay = body.Start.Date != null
    };

    db.Events.Add(newEvent);
    await db.SaveChangesAsync();
    Console.WriteLine($"[SIM] Created event: {newEvent.Id} ({newEvent.Summary})");

    return Results.Ok(new
    {
        id = newEvent.Id,
        status = "confirmed",
        summary = newEvent.Summary,
        location = newEvent.Location,
        description = newEvent.Description,
        start = newEvent.IsAllDay ? (object)new { date = newEvent.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = newEvent.StartTime.ToString("O") },
        end = newEvent.IsAllDay ? (object)new { date = newEvent.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = newEvent.EndTime.ToString("O") }
    });
});

// Persistent Update Event
app.MapPut("/calendars/{calendarId}/events/{eventId}", async (string calendarId, string eventId, HttpRequest request, SimContext db) =>
{
    Console.WriteLine($"[SIM] PUT update event: {eventId} for calendar: {calendarId}");
    // Be flexible: search by ID only. In a real system the ID is unique across all calendars.
    var existing = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId);

    // Flexibility for seed/evt mismatch
    if (existing == null)
    {
        string altId = eventId.Contains("seed") ? eventId.Replace("seed_", "") : (eventId.StartsWith("evt_") ? eventId.Replace("evt_", "evt_seed_") : eventId);
        existing = await db.Events.FirstOrDefaultAsync(e => e.Id == altId);
    }

    if (existing == null) 
    {
        Console.WriteLine($"[SIM] Error: Event {eventId} not found (tried alt too).");
        return Results.NotFound();
    }
    
    // Optional: update calendarId if it moved (or just keep existing)
    existing.CalendarId = calendarId;

    var body = await request.ReadFromJsonAsync<GoogleEventRequest>();
    if (body == null) 
    {
        Console.WriteLine("[SIM] Error: Failed to deserialize request body.");
        return Results.BadRequest();
    }

    existing.Summary = body.Summary ?? existing.Summary;
    existing.Location = body.Location;
    existing.Description = body.Description;
    existing.StartTime = body.Start.DateTime ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date) : existing.StartTime);
    existing.EndTime = body.End.DateTime ?? (body.End.Date != null ? DateTime.Parse(body.End.Date) : existing.EndTime);
    existing.IsAllDay = body.Start.Date != null;

    await db.SaveChangesAsync();
    Console.WriteLine($"[SIM] Updated event: {existing.Id} ({existing.Summary}) on calendar: {existing.CalendarId}");

    return Results.Ok(new
    {
        id = existing.Id,
        status = "confirmed",
        summary = existing.Summary,
        location = existing.Location,
        description = existing.Description,
        start = existing.IsAllDay ? (object)new { date = existing.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = existing.StartTime.ToString("O") },
        end = existing.IsAllDay ? (object)new { date = existing.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = existing.EndTime.ToString("O") }
    });
});

// Persistent Delete Event
app.MapDelete("/calendars/{calendarId}/events/{eventId}", async (string calendarId, string eventId, SimContext db) =>
{
    Console.WriteLine($"[SIM] DELETE event: {eventId} for calendar: {calendarId}");
    var existing = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId);

    // Flexibility for seed/evt mismatch
    if (existing == null)
    {
        string altId = eventId.Contains("seed") ? eventId.Replace("seed_", "") : (eventId.StartsWith("evt_") ? eventId.Replace("evt_", "evt_seed_") : eventId);
        existing = await db.Events.FirstOrDefaultAsync(e => e.Id == altId);
    }

    if (existing == null) 
    {
        Console.WriteLine($"[SIM] Error: Event {eventId} not found (tried alt too).");
        return Results.NotFound();
    }

    db.Events.Remove(existing);
    await db.SaveChangesAsync();
    Console.WriteLine($"[SIM] Deleted event: {eventId}");

    return Results.NoContent();
});

// Mock Webhooks: Trigger a change back to the backend
app.MapPost("/simulate/push", async (HttpContext context) =>
{
    using var client = new HttpClient();
    var webApiUrl = "https://localhost:7196/api/sync/webhook";
    var webhookRequest = new HttpRequestMessage(HttpMethod.Post, webApiUrl);
    webhookRequest.Headers.Add("x-goog-resource-state", "sync");
    webhookRequest.Headers.Add("x-goog-resource-id", "simulated_resource_" + Guid.NewGuid().ToString());
    
    try
    {
        var result = await client.SendAsync(webhookRequest);
        return Results.Ok($"Webhook sent to {webApiUrl}. Status: {result.StatusCode}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to send webhook: {ex.Message}");
    }
});

app.Run();

void SeedData(SimContext db)
{
    if (db.Events.Any()) return;

    var now = DateTime.UtcNow;
    var startOfMonth = new DateTime(now.Year, now.Month, 1);

    db.Events.AddRange(
        new SimulatedEvent
        {
            Id = "evt_seed_1",
            CalendarId = "simulated_calendar_family",
            Summary = "Dentist Appointment",
            Location = "123 Main St",
            StartTime = startOfMonth.AddDays(10).AddHours(14),
            EndTime = startOfMonth.AddDays(10).AddHours(15),
            IsAllDay = false
        },
        new SimulatedEvent
        {
            Id = "evt_seed_2",
            CalendarId = "simulated_calendar_family",
            Summary = "Family Dinner",
            Location = "Home",
            StartTime = startOfMonth.AddDays(15).AddHours(18),
            EndTime = startOfMonth.AddDays(15).AddHours(20),
            IsAllDay = false
        },
        new SimulatedEvent
        {
            Id = "evt_seed_3",
            CalendarId = "simulated_calendar_work",
            Summary = "Project Sync",
            Location = "Microsoft Teams",
            StartTime = startOfMonth.AddDays(12).AddHours(9),
            EndTime = startOfMonth.AddDays(12).AddHours(10),
            IsAllDay = false
        },
        new SimulatedEvent
        {
            Id = "evt_seed_4",
            CalendarId = "simulated_calendar_work",
            Summary = "Quarterly Review",
            Location = "Conference Room A",
            StartTime = startOfMonth.AddDays(20).AddHours(13),
            EndTime = startOfMonth.AddDays(20).AddHours(15),
            IsAllDay = false
        },
        new SimulatedEvent
        {
            Id = "evt_seed_5",
            CalendarId = "simulated_calendar_family",
            Summary = "School Holiday",
            StartTime = startOfMonth.AddDays(5),
            EndTime = startOfMonth.AddDays(6),
            IsAllDay = true
        }
    );

    db.SaveChanges();
    Console.WriteLine("[SIM] Data seeded.");
}

namespace FamilyHQ.Simulator
{
    public partial class Program
    {
        public static List<object> ActiveCalendars = new()
        {
            new { id = "simulated_calendar_family", summary = "Family Calendar", backgroundColor = "#b39ddb" },
            new { id = "simulated_calendar_work", summary = "Work Calendar", backgroundColor = "#9e9e9e" }
        };
    }
}