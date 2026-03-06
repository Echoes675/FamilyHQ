using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Dummy OAuth Token Exchange / Refresh
app.MapPost("/oauth2/v4/token", async context =>
{
    var response = new
    {
        access_token = "simulated_access_token_" + Guid.NewGuid().ToString("N"),
        refresh_token = "simulated_refresh_token",
        expires_in = 3600,
        token_type = "Bearer"
    };
    
    await context.Response.WriteAsJsonAsync(response);
});

// Dummy Calendar List
app.MapGet("/users/me/calendarList", async context =>
{
    var response = new
    {
        items = new[]
        {
            new
            {
                id = "simulated_calendar_family",
                summary = "Family Calendar",
                backgroundColor = "#b39ddb"
            },
            new
            {
                id = "simulated_calendar_work",
                summary = "Work Calendar",
                backgroundColor = "#9e9e9e"
            }
        }
    };
    
    await context.Response.WriteAsJsonAsync(response);
});

// Dummy Events List
app.MapGet("/calendars/{calendarId}/events", async context =>
{
    var calendarId = context.Request.RouteValues["calendarId"]?.ToString();
    var syncToken = context.Request.Query["syncToken"].ToString();
    
    var items = new List<object>();

    if (string.IsNullOrEmpty(syncToken))
    {
        // First full sync - return fresh simulated events
        items.Add(new
        {
            id = "evt_1",
            status = "confirmed",
            summary = "Dentist Appointment",
            location = "123 Main St",
            start = new { dateTime = DateTime.UtcNow.AddDays(2).ToString("O") },
            end = new { dateTime = DateTime.UtcNow.AddDays(2).AddHours(1).ToString("O") }
        });
        
        items.Add(new
        {
            id = "evt_2",
            status = "confirmed",
            summary = "School Holiday",
            start = new { date = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd") },
            end = new { date = DateTime.UtcNow.AddDays(6).ToString("yyyy-MM-dd") }
        });
    }
    else
    {
        // Incremental sync logic (simulate an update or deletion)
        items.Add(new
        {
            id = "evt_3_incremental",
            status = "confirmed",
            summary = "Surprise Party (Updated)",
            start = new { dateTime = DateTime.UtcNow.AddDays(1).ToString("O") },
            end = new { dateTime = DateTime.UtcNow.AddDays(1).AddHours(2).ToString("O") }
        });
    }

    var response = new
    {
        items = items,
        nextSyncToken = "simulated_sync_token_" + Guid.NewGuid().ToString("N")
    };
    
    await context.Response.WriteAsJsonAsync(response);
});

// Mock Webhooks: Trigger a change back to the backend
app.MapPost("/simulate/push", async (HttpContext context) =>
{
    // The WebApi url would likely be https://localhost:7196/api/sync/webhook
    // This is just a dev endpoint to simulate hitting that webhook.
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
