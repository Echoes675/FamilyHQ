using FamilyHQ.Core.Logging;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using FamilyHQ.Simulator.Middleware;
using FamilyHQ.Simulator.State;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog: Console (Docker stdout) + prod-safe Seq sink.
// Verbosity is still governed by the existing Logging:LogLevel section.
builder.Host.UseSerilog((context, loggerConfiguration) =>
    SerilogConfigurator.Configure(
        loggerConfiguration,
        context.Configuration,
        application: "FamilyHQ.Simulator",
        environment: builder.Environment.EnvironmentName));

builder.Services.AddControllers();
builder.Services.AddDbContext<SimContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<SyncFailureModeStore>();
builder.Services.AddSingleton<OutboundWriteCountStore>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();

app.MapControllers();

// Apply migrations and seed initial data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SimContext>();
    db.Database.Migrate();
    DataSeeder.SeedData(db);
}
// Removed all inline maps because they have been extracted to Controllers.
app.Run();