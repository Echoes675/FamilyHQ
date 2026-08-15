using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
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

        // Sun Calculator
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISunCalculatorService) &&
            sd.ImplementationType == typeof(SunCalculatorService) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        // Day Theme
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IDayThemeService) &&
            sd.ImplementationType == typeof(DayThemeService) &&
            sd.Lifetime == ServiceLifetime.Scoped);

        // Hosted Service
        // AddHostedService registers IHostedService with the implementation type
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(SyncOrchestrator) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddFamilyHqServices_GoogleHttpClients_GetDefaultTimeouts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddFamilyHqServices(configuration);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // AddHttpClient<T> registers a named client using the type's short name; the typed
        // client receives exactly this configured HttpClient.
        factory.CreateClient(nameof(GoogleAuthService)).Timeout.Should().Be(TimeSpan.FromSeconds(30));
        factory.CreateClient(nameof(GoogleCalendarClient)).Timeout.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void AddFamilyHqServices_GoogleTimeoutsConfigured_AppliesConfiguredValues()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("GoogleResilience:AuthTimeout", "00:00:07"),
                new KeyValuePair<string, string?>("GoogleResilience:CalendarTimeout", "00:00:09")
            })
            .Build();
        services.AddFamilyHqServices(configuration);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        factory.CreateClient(nameof(GoogleAuthService)).Timeout.Should().Be(TimeSpan.FromSeconds(7));
        factory.CreateClient(nameof(GoogleCalendarClient)).Timeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void AddFamilyHqServices_InvalidGoogleResilienceConfig_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("GoogleResilience:CalendarTimeout", "00:00:00")
            })
            .Build();

        // Fail-fast: a zero/negative timeout must surface at boot, not as a hung/instantly-failing call.
        services.Invoking(s => s.AddFamilyHqServices(configuration))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*CalendarTimeout*");
    }

    [Fact]
    public void AddFamilyHqServices_IGoogleCalendarClient_ResolvesToResilientDecorator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddFamilyHqServices(configuration);
        // These are registered by WebApi/FamilyHQ.Data.PostgreSQL (not AddFamilyHqServices); stub them
        // so the inner client's dependency chain (GoogleCalendarClient -> TimeZoneService -> ...) can build.
        services.AddScoped(_ => Mock.Of<ITokenStore>());
        services.AddScoped(_ => Mock.Of<ICurrentUserService>());
        services.AddScoped(_ => Mock.Of<IDisplaySettingRepository>());
        services.AddScoped(_ => Mock.Of<ILocationSettingRepository>());
        services.AddScoped(_ => Mock.Of<ILocationService>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IGoogleCalendarClient>();

        client.Should().BeOfType<ResilientGoogleCalendarClient>();
    }
}
