using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL;
using FamilyHQ.Services;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using FamilyHQ.Core.Logging;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging via Serilog: Console (Docker stdout) + prod-safe Seq sink.
// Verbosity is still governed by the existing Logging:LogLevel section.
builder.Host.UseSerilog((context, loggerConfiguration) =>
    SerilogConfigurator.Configure(
        loggerConfiguration,
        context.Configuration,
        application: "FamilyHQ.WebApi",
        environment: builder.Environment.EnvironmentName));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Single point that maps typed domain exceptions to HTTP status codes (FHQ-39). RFC7807
// ProblemDetails are emitted for handled domain exceptions; anything else falls through to 500.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FamilyHQ.WebApi.Middleware.DomainExceptionHandler>();

// Configure Services
builder.Services.Configure<GoogleCalendarOptions>(builder.Configuration.GetSection(GoogleCalendarOptions.SectionName));

// Add database
builder.Services.AddPostgreSqlDataAccess(builder.Configuration);

// Add auth context access
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, FamilyHQ.WebApi.Services.CurrentUserService>();

// Add our core business logic
builder.Services.AddFamilyHqServices(builder.Configuration);

// Register typed HttpClients for services that require an injected HttpClient
var ipApiBaseUrl = builder.Configuration["Location:IpApiBaseUrl"] ?? "http://ip-api.com";
builder.Services.AddHttpClient<ILocationService, LocationService>(client =>
{
    client.BaseAddress = new Uri(ipApiBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var geocodingBaseUrl = builder.Configuration["Geocoding:BaseUrl"]
    ?? "https://nominatim.openstreetmap.org";
builder.Services.AddHttpClient<IGeocodingService, GeocodingService>(client =>
{
    client.BaseAddress = new Uri(geocodingBaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyHQ/1.0");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Add Data Protection with database key storage and certificate-based key encryption
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToDbContext<FamilyHqDbContext>();

var dpCertPath = builder.Configuration["DataProtection:CertificatePath"];
var dpCertPassword = builder.Configuration["DataProtection:CertificatePassword"];

if (!string.IsNullOrEmpty(dpCertPath) && !string.IsNullOrEmpty(dpCertPassword))
{
    var certificate = X509CertificateLoader.LoadPkcs12FromFile(dpCertPath, dpCertPassword);
    dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DataProtection:CertificatePath and DataProtection:CertificatePassword must be configured in non-development environments.");
}

// Register DatabaseTokenStore (overrides the FileTokenStore from AddFamilyHqServices)
// DatabaseTokenStore is scoped because it depends on DbContext which is scoped
builder.Services.AddScoped<ITokenStore, DatabaseTokenStore>();

// Add SignalR Configuration
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<FamilyHQ.Core.Interfaces.IThemeBroadcaster, FamilyHQ.WebApi.Hubs.SignalRThemeBroadcaster>();
builder.Services.AddSingleton<FamilyHQ.Core.Interfaces.IWeatherBroadcaster, FamilyHQ.WebApi.Hubs.SignalRWeatherBroadcaster>();
builder.Services.AddSingleton<FamilyHQ.Core.Interfaces.IConnectionStatusBroadcaster, FamilyHQ.WebApi.Hubs.SignalRConnectionStatusBroadcaster>();

// Add background webhook renewal service
builder.Services.AddHostedService<FamilyHQ.WebApi.Services.WebhookRenewalService>();

// Add background job that prunes terminal (Completed/Failed) sync jobs older than
// SyncOptions.TerminalJobRetention, so CalendarSyncJobs doesn't grow unbounded (FHQ-137)
builder.Services.AddHostedService<FamilyHQ.WebApi.Services.SyncJobPruningService>();

// Add background worker that drains the durable calendar sync queue
builder.Services.AddHostedService<FamilyHQ.WebApi.Services.CalendarSyncWorker>();

// Add Authentication for the Simulator
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("JWT signing key is not configured.");

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Prevent the middleware from remapping JWT claim names (e.g. "sub" → ClaimTypes.NameIdentifier).
        // CurrentUserService reads the "sub" claim directly via JwtRegisteredClaimNames.Sub.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "FamilyHQ",
            ValidateAudience = true,
            ValidAudience = "FamilyHQ",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSigningKey))
        };
        
        // SignalR Authentication
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/calendar"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// CORS — use FrontendBaseUrl from configuration
var frontendBaseUrl = builder.Configuration["FrontendBaseUrl"]
    ?? throw new InvalidOperationException("FrontendBaseUrl must be configured.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorApp", policy =>
    {
        policy.WithOrigins(frontendBaseUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // SignalR requires credentials
    });
});

if (builder.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FamilyHQ.Data.FamilyHqDbContext>();
    db.Database.Migrate();
}

if (app.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
// Maps typed domain exceptions to ProblemDetails (FHQ-39). Registered inside GlobalExceptionMiddleware
// so domain exceptions are turned into 4xx here; any non-domain exception this handler declines falls
// through to GlobalExceptionMiddleware and surfaces as a 500.
app.UseExceptionHandler();
app.UseMiddleware<RequestTimingMiddleware>();

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

if (!app.Configuration.GetValue<bool>("ReverseProxy:Enabled"))
{
    app.UseHttpsRedirection();
}
app.UseCors("AllowBlazorApp");

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Map SignalR Hub
app.MapHub<CalendarHub>("/hubs/calendar");

app.Run();
