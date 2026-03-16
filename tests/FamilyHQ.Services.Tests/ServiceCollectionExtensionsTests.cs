using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FamilyHQ.Services.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddFamilyHqServices_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Sync:PeriodicSyncInterval", "01:00:00")
            })
            .Build();

        // Act
        services.AddFamilyHqServices(configuration);

        // Assert
        // Token Store - No longer registered here (DatabaseTokenStore is registered in WebApi/Program.cs)
        services.Should().NotContain(sd => sd.ServiceType == typeof(ITokenStore));

        // HttpClients use a typed client factory which registers the type itself as Transient
        services.Should().Contain(sd => 
            sd.ServiceType == typeof(GoogleAuthService) && 
            sd.Lifetime == ServiceLifetime.Transient);

        services.Should().Contain(sd => 
            sd.ServiceType == typeof(IGoogleCalendarClient) && 
            sd.Lifetime == ServiceLifetime.Transient);

        // Calendar Sync
        services.Should().Contain(sd => 
            sd.ServiceType == typeof(ICalendarSyncService) && 
            sd.ImplementationType == typeof(CalendarSyncService) && 
            sd.Lifetime == ServiceLifetime.Scoped);

        // Hosted Service
        // AddHostedService registers IHostedService with the implementation type
        services.Should().Contain(sd => 
            sd.ServiceType == typeof(IHostedService) && 
            sd.ImplementationType == typeof(SyncOrchestrator) && 
            sd.Lifetime == ServiceLifetime.Singleton);
    }
}
