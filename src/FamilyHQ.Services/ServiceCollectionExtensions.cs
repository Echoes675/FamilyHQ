using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHQ.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFamilyHqServices(this IServiceCollection services)
    {
        services.AddSingleton<ITokenStore, FileTokenStore>();
        
        services.AddHttpClient<GoogleAuthService>();
        
        return services;
    }
}
