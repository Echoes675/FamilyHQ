using FamilyHQ.Data.PostgreSQL;
using FamilyHQ.Services;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure Services
builder.Services.Configure<GoogleCalendarOptions>(builder.Configuration.GetSection(GoogleCalendarOptions.SectionName));

// Add database
builder.Services.AddPostgreSqlDataAccess(builder.Configuration);

// Add our core business logic
builder.Services.AddFamilyHqServices();

// Add SignalR Configuration
builder.Services.AddSignalR();

// CORS is required because Blazor WASM might run on a different port in dev
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorApp", policy =>
    {
        policy.WithOrigins("https://localhost:7154", "http://localhost:5154")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // SignalR requires credentials
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FamilyHQ.Data.FamilyHqDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Minimal API setup or Swagger could be here
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorApp");

app.UseAuthorization();
app.MapControllers();

// Map SignalR Hub
app.MapHub<CalendarHub>("/hubs/calendar");

app.Run();
