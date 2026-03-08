using FamilyHQ.Data.PostgreSQL;
using FamilyHQ.Services;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Middleware;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
    app.MapOpenApi();
    app.MapScalarApiReference(options => 
    {
        options.WithTitle("FamilyHQ Backend API");
        options.WithTheme(Scalar.AspNetCore.ScalarTheme.DeepSpace);
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowBlazorApp");

app.UseAuthorization();
app.MapControllers();

// Map SignalR Hub
app.MapHub<CalendarHub>("/hubs/calendar");

app.Run();
