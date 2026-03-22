using System.Security.Cryptography.X509Certificates;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL;
using FamilyHQ.Services;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using FamilyHQ.Core.Constants;

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

// Add auth context access
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, FamilyHQ.WebApi.Services.CurrentUserService>();

// Add our core business logic
builder.Services.AddFamilyHqServices(builder.Configuration);

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
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSigningKey))
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FamilyHQ.Data.FamilyHqDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<CorrelationIdMiddleware>();
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
