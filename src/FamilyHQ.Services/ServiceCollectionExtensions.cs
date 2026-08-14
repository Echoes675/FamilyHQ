using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using FamilyHQ.Services.Weather;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFamilyHqServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));

        // FHQ-91: bind eagerly and fail-fast at boot (JwtSessionOptions precedent) — a bad timeout
        // must not surface as a hung or instantly-cancelled Google call at runtime.
        var googleResilience = configuration
            .GetSection(Options.GoogleResilienceOptions.SectionName)
            .Get<Options.GoogleResilienceOptions>() ?? new Options.GoogleResilienceOptions();
        googleResilience.Validate();

        // FHQ-91: explicit per-attempt timeouts (HttpClient's 100s default held the sync worker /
        // a login request hostage on a hung Google endpoint). Worst-case wall-time math lives on
        // GoogleResilienceOptions.CalendarTimeout.
        services.AddHttpClient<GoogleAuthService>(client => client.Timeout = googleResilience.AuthTimeout);
        services.AddSingleton<IIdTokenValidator, JwksIdTokenValidator>();
        // FHQ-154: register the concrete typed client, then decorate it with the retry wrapper.
        services.AddHttpClient<GoogleCalendarClient>(client => client.Timeout = googleResilience.CalendarTimeout);
        services.Configure<Options.GoogleResilienceOptions>(
            configuration.GetSection(Options.GoogleResilienceOptions.SectionName));
        services.AddTransient<IGoogleCalendarClient>(sp => new Calendar.ResilientGoogleCalendarClient(
            sp.GetRequiredService<GoogleCalendarClient>(),
            sp.GetRequiredService<IOptions<Options.GoogleResilienceOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<Calendar.ResilientGoogleCalendarClient>>()));

        services.AddScoped<IMemberTagParser, MemberTagParser>();
        services.AddScoped<ICalendarMigrationService, CalendarMigrationService>();
        services.AddScoped<IPlacementReconciler, PlacementReconciler>();
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        services.AddScoped<IWebhookRegistrationService, WebhookRegistrationService>();
        services.AddScoped<ICalendarEventService, CalendarEventService>();
        services.AddHostedService<SyncOrchestrator>();
        services.Configure<DayThemeOptions>(configuration.GetSection(DayThemeOptions.SectionName));
        services.AddSingleton<ISunCalculatorService, SunCalculatorService>();
        services.AddScoped<IDayThemeService, DayThemeService>();
        services.AddSingleton<DayThemeSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<DayThemeSchedulerService>());
        services.AddSingleton<IDayThemeScheduler>(sp => sp.GetRequiredService<DayThemeSchedulerService>());

        services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));

        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<WeatherOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IWeatherService, WeatherService>();
        services.AddScoped<IWeatherRefreshService, WeatherRefreshService>();
        services.AddHostedService<WeatherPollerService>();

        // Webhook self-echo guard (FHQ-30): singleton cache survives across scoped sync requests.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenCache, AccessTokenCache>();
        services.TryAddSingleton<ITimeZoneLookup, GeoTimeZoneLookup>();
        services.AddSingleton<IOutboundWriteHashCache, OutboundWriteHashCache>();
        services.AddSingleton<ISyncJobSignal, SyncJobSignal>();

        services.AddMemoryCache();
        services.AddScoped<ITimeZoneService, TimeZoneService>();

        return services;
    }
}
