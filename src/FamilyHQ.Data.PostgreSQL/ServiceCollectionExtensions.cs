using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHQ.Data.PostgreSQL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSqlDataAccess(
        this IServiceCollection services, 
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection")
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);
        
        services.AddDbContext<FamilyHqDbContext>(options =>
            options.UseNpgsql(connectionString, x => x.MigrationsAssembly(typeof(ServiceCollectionExtensions).Assembly.FullName)));

        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<IDayThemeRepository, DayThemeRepository>();
        services.AddScoped<ILocationSettingRepository, LocationSettingRepository>();

        return services;
    }
}
