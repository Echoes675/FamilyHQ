using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Theme;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests;

public class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// FHQ-166. At least <see cref="SaltedHashPiiRedactor.MinimumSaltLength"/> characters, because
    /// registration now rejects anything shorter.
    /// </summary>
    private const string ConfiguredSalt = "a-configured-salt-of-sufficient-length";

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

    // ---- FHQ-114: ip-api / Nominatim / Open-Meteo transient-fault retry ----
    [Fact]
    public void AddFamilyHqServices_ExternalHttpClients_GetTheirTotalRetryBudgetAsTimeout()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddFamilyHqServices(configuration);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // The retry handler sleeps INSIDE SendAsync, so each client's Timeout is the total budget
        // for the whole attempt+backoff sequence, not a single attempt. Geocoding stays near its
        // pre-retry 10s ceiling; Open-Meteo keeps the 30s it always had. (FHQ-179 removed the
        // ip-api client — a geolocation lookup from this container resolves the hosting VPS.)
        factory.CreateClient(nameof(IGeocodingService)).Timeout.Should().Be(TimeSpan.FromSeconds(12));
        factory.CreateClient(nameof(IWeatherProvider)).Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddFamilyHqServices_ExternalHttpClients_AreRegisteredWithBaseAddresses()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Geocoding:BaseUrl", "https://nominatim.test"),
                new KeyValuePair<string, string?>("Weather:BaseUrl", "https://openmeteo.test")
            })
            .Build();
        services.AddFamilyHqServices(configuration);

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        factory.CreateClient(nameof(IGeocodingService)).BaseAddress.Should().Be(new Uri("https://nominatim.test/"));
        factory.CreateClient(nameof(IWeatherProvider)).BaseAddress.Should().Be(new Uri("https://openmeteo.test/"));
    }

    [Fact]
    public void AddFamilyHqServices_InvalidExternalHttpResilienceConfig_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ExternalHttpResilience:MaxAttempts", "0")
            })
            .Build();

        services.Invoking(s => s.AddFamilyHqServices(configuration))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxAttempts*");
    }

    // ---- FHQ-109: weather poll backoff options are fail-fast validated at boot ----
    [Fact]
    public void AddFamilyHqServices_InvalidWeatherConfig_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Weather:MaxFailureBackoffMinutes", "0")
            })
            .Build();

        services.Invoking(s => s.AddFamilyHqServices(configuration))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxFailureBackoffMinutes*");
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

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<IGoogleCalendarClient>();

        client.Should().BeOfType<ResilientGoogleCalendarClient>();
    }

    // FHQ-115: OpenMeteoWeatherProvider gained an IWmoCodeMapper dependency. If its
    // registration were dropped, the first resolve would happen inside WeatherPollerService
    // 5s after startup and be swallowed by that service's per-user catch — weather would
    // simply never appear, with nothing to point at the cause.
    [Fact]
    public void AddFamilyHqServices_IWeatherProvider_ResolvesWithItsWmoCodeMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddFamilyHqServices(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IWeatherProvider>().Should().BeOfType<OpenMeteoWeatherProvider>();
        provider.GetRequiredService<IWmoCodeMapper>().Should().BeOfType<WmoCodeMapper>();
    }

    // FHQ-166: the redactor must be a SINGLETON reading the configured salt. Registered per-scope it
    // would still redact, but the same calendar would carry a different token in every request, and
    // the correlation the redaction exists to preserve would be silently gone.
    [Fact]
    public void AddFamilyHqServices_IPiiRedactor_IsASingletonUsingTheConfiguredSalt()
    {
        var services = CreateServicesWithSalt(ConfiguredSalt);

        // ContainSingle, not Contain: a later scoped registration of the same service type would
        // win at resolution while leaving a singleton descriptor behind for a weaker assertion to
        // find, and the per-request tokens this test exists to prevent would be back.
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IPiiRedactor))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IPiiRedactor>();

        resolved.Should().BeOfType<SaltedHashPiiRedactor>();
        resolved.Redact("a.family.member@example.com").Should().Be(
            new SaltedHashPiiRedactor(ConfiguredSalt, Mock.Of<ILogger<SaltedHashPiiRedactor>>())
                .Redact("a.family.member@example.com"),
            $"the registration must actually read {SaltedHashPiiRedactor.SaltConfigurationKey}, not ignore it");
    }

    [Fact]
    public void AddFamilyHqServices_IPiiRedactor_IsTheSameInstanceInEveryScope()
    {
        // The lifetime assertion above is about the descriptor; this is about what callers get. Two
        // scopes, one instance — otherwise the same calendar redacts to a different token in every
        // request and nothing in Seq joins up.
        using var provider = CreateServicesWithSalt(ConfiguredSalt).BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        ReferenceEquals(
            first.ServiceProvider.GetRequiredService<IPiiRedactor>(),
            second.ServiceProvider.GetRequiredService<IPiiRedactor>())
            .Should().BeTrue("every scope must share one redactor, and therefore one salt");
    }

    [Fact]
    public void AddFamilyHqServices_WithASaltTooShortToBeWorthHaving_FailsAtRegistrationNotAtFirstUse()
    {
        // FHQ-91 precedent: bad configuration must break the deployment, not the first sync. The
        // factory alone would defer this until GoogleCalendarClient was first resolved, hours later.
        var register = () => CreateServicesWithSalt("short");

        register.Should().Throw<ArgumentException>()
            .WithMessage($"*{SaltedHashPiiRedactor.SaltConfigurationKey}*");
    }

    private static ServiceCollection CreateServicesWithSalt(string salt)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>(SaltedHashPiiRedactor.SaltConfigurationKey, salt)
            })
            .Build();
        services.AddFamilyHqServices(configuration);

        return services;
    }
}
