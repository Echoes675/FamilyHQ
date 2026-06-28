using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.Services.Auth;
using FamilyHQ.WebUi.Services.Correlation;
using FamilyHQ.WebUi.Configuration;

namespace FamilyHQ.WebUi;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        var backendUrl = builder.Configuration["BackendUrl"] ?? "https://localhost:5001";

        var featureFlags = new FeatureFlags
        {
            WeatherOverrideEnabled = builder.Configuration.GetValue<bool>("FeatureWeatherOverride"),
            ClockOverrideEnabled = builder.Configuration.GetValue<bool>("FeatureClockOverride")
        };
        builder.Services.AddSingleton(featureFlags);

        builder.Services.AddScoped<IAuthTokenStore, LocalStorageAuthTokenStore>();
        builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
        builder.Services.AddScoped<ICorrelationIdTokenStore, LocalStorageCorrelationIdTokenStore>();
        builder.Services.AddTransient<CorrelationIdMessageHandler>();
        builder.Services.AddTransient<CustomAuthorizationMessageHandler>();

        builder.Services.AddHttpClient<ICalendarApiService, CalendarApiService>(client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        builder.Services.AddSingleton(sp => new SignalRService(backendUrl));
        builder.Services.AddSingleton<ISignalRConnectionEvents>(sp => sp.GetRequiredService<SignalRService>());

        builder.Services.AddHttpClient<IThemeService, ThemeService>(client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        builder.Services.AddHttpClient<ISettingsApiService, SettingsApiService>(client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        builder.Services.AddHttpClient<IDiagnosticsApiService, DiagnosticsApiService>(client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        // Anonymous client used before a JWT is available (e.g. the exchange-code flow).
        // No auth or correlation handlers — the token has not been issued yet.
        builder.Services.AddHttpClient("Auth", client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        });

        builder.Services.AddHttpClient("Weather", client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        builder.Services.AddHttpClient("Version", client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>();

        // FHQ-63: wrap the system clock so lower environments can advance the displayed day.
        // Registered as both TimeProvider (for existing consumers like VersionService) and the
        // concrete type (so Index can drive the dev bridge). In production the override is off.
        var kioskClock = new KioskTimeProvider(TimeProvider.System, featureFlags.ClockOverrideEnabled);
        builder.Services.AddSingleton<TimeProvider>(kioskClock);
        builder.Services.AddSingleton(kioskClock);

        builder.Services.AddSingleton<IVersionService>(sp => new VersionService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Version"),
            sp.GetRequiredService<IJSRuntime>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<VersionService>>(),
            sp.GetRequiredService<ISignalRConnectionEvents>()));

        builder.Services.AddScoped<IWeatherUiService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("Weather");
            var signalR = sp.GetRequiredService<SignalRService>();
            var logger = sp.GetRequiredService<ILogger<WeatherUiService>>();
            return new WeatherUiService(client, signalR, logger);
        });

        builder.Services.AddScoped<IDisplaySettingService, DisplaySettingService>();
        builder.Services.AddScoped<IWeatherOverrideService, WeatherOverrideService>();

        var host = builder.Build();
        await host.Services.GetRequiredService<IVersionService>().InitializeAsync();
        await host.RunAsync();
    }
}
