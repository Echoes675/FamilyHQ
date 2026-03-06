using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHQ.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFamilyHqServices(this IServiceCollection services)
    {
        services.AddSingleton<ITokenStore, FileTokenStore>();
        
        services.AddHttpClient<GoogleAuthService>();
        services.AddHttpClient<IGoogleCalendarClient, GoogleCalendarClient>();
        
        services.AddScoped<ICalendarSyncService, CalendarSyncService>();
        services.AddHostedService<SyncOrchestrator>();
        
        return services;
    }
}
