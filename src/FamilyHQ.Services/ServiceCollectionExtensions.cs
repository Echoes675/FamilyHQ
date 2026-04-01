using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHQ.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFamilyHqServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SyncOptions>(configuration.GetSection(SyncOptions.SectionName));

        services.AddHttpClient<GoogleAuthService>();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>();

        services.AddScoped<IAccessTokenProvider, AccessTokenProvider>();
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        services.AddScoped<ICalendarEventService, CalendarEventService>();
        services.AddHostedService<SyncOrchestrator>();
        services.AddSingleton<ISunCalculatorService, SunCalculatorService>();
        services.AddScoped<IDayThemeService, DayThemeService>();
        services.AddSingleton<DayThemeSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<DayThemeSchedulerService>());
        services.AddSingleton<IDayThemeScheduler>(sp => sp.GetRequiredService<DayThemeSchedulerService>());

        return services;
    }
}
