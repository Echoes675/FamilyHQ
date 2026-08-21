using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Http;
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
        // FHQ-166: the log-seam redactor. Singleton — the salt is read once at boot and the redactor
        // is stateless thereafter, so every consumer must share the instance or the same calendar
        // would hash differently in different scopes.
        //
        // Validated eagerly here (the FHQ-91 precedent above): the factory below does not run until
        // something first resolves IPiiRedactor, which in practice is GoogleCalendarClient on the
        // first sync — a salt too short to be worth having must fail the deployment, not a sync.
        var redactionSalt = configuration[Core.Logging.SaltedHashPiiRedactor.SaltConfigurationKey];
        Core.Logging.SaltedHashPiiRedactor.ValidateSalt(redactionSalt);
        services.AddSingleton<IPiiRedactor>(sp => new Core.Logging.SaltedHashPiiRedactor(
            redactionSalt,
            sp.GetRequiredService<ILogger<Core.Logging.SaltedHashPiiRedactor>>()));

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

        // FHQ-109: bind eagerly and fail-fast at boot — a poll interval or backoff cap that cannot
        // work must surface here, not as a hot-looping (or never-waking) weather poller.
        var weatherOptions = configuration
            .GetSection(WeatherOptions.SectionName)
            .Get<WeatherOptions>() ?? new WeatherOptions();
        weatherOptions.Validate();
        services.Configure<WeatherOptions>(configuration.GetSection(WeatherOptions.SectionName));

        // FHQ-114: transient-fault retry for the three non-Google outbound clients. Each client's
        // Timeout is the TOTAL budget for the attempt+backoff sequence, because the handler sleeps
        // inside SendAsync — worst-case arithmetic lives on ExternalHttpResilienceOptions.
        var externalResilience = configuration
            .GetSection(ExternalHttpResilienceOptions.SectionName)
            .Get<ExternalHttpResilienceOptions>() ?? new ExternalHttpResilienceOptions();
        externalResilience.Validate();
        services.Configure<ExternalHttpResilienceOptions>(
            configuration.GetSection(ExternalHttpResilienceOptions.SectionName));
        services.AddTransient<TransientHttpRetryHandler>();

        // Stateless pure lookup — singleton. Injected into OpenMeteoWeatherProvider by the typed
        // client below (FHQ-115).
        services.AddSingleton<IWmoCodeMapper, WmoCodeMapper>();

        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(client =>
        {
            client.BaseAddress = new Uri(weatherOptions.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = externalResilience.WeatherTimeout;
        }).AddHttpMessageHandler<TransientHttpRetryHandler>();

        // ip-api geolocation and Nominatim geocoding: service-layer HTTP clients, wired here rather
        // than in WebApi/Program.cs so all three share one resilience configuration (FHQ-114).
        var ipApiBaseUrl = configuration["Location:IpApiBaseUrl"] ?? "http://ip-api.com";
        services.AddHttpClient<ILocationService, LocationService>(client =>
        {
            client.BaseAddress = new Uri(ipApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = externalResilience.LocationTimeout;
        }).AddHttpMessageHandler<TransientHttpRetryHandler>();

        var geocodingBaseUrl = configuration["Geocoding:BaseUrl"] ?? "https://nominatim.openstreetmap.org";
        services.AddHttpClient<IGeocodingService, GeocodingService>(client =>
        {
            client.BaseAddress = new Uri(geocodingBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyHQ/1.0");
            client.Timeout = externalResilience.GeocodingTimeout;
        }).AddHttpMessageHandler<TransientHttpRetryHandler>();

        services.AddScoped<IWeatherService, WeatherService>();
        services.AddScoped<IWeatherRefreshService, WeatherRefreshService>();
        services.AddHostedService<WeatherPollerService>();

        // Webhook self-echo guard (FHQ-30): singleton cache survives across scoped sync requests.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenCache, AccessTokenCache>();
        services.TryAddSingleton<ITimeZoneLookup, GeoTimeZoneLookup>();
        // FHQ-161: stateless tzdb lookup behind an interface so recurrence enumeration can be
        // zone-anchored in production and substituted in unit tests.
        services.TryAddSingleton<IRecurrenceTimeZoneFactory, Calendar.NodaTimeRecurrenceTimeZoneFactory>();
        services.AddSingleton<IOutboundWriteHashCache, OutboundWriteHashCache>();
        services.AddSingleton<ISyncJobSignal, SyncJobSignal>();

        services.AddMemoryCache();
        services.AddScoped<ITimeZoneService, TimeZoneService>();

        return services;
    }
}
