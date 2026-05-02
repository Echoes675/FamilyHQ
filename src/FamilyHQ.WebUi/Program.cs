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
            WeatherOverrideEnabled = builder.Configuration.GetValue<bool>("FeatureWeatherOverride")
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

        builder.Services.AddSingleton(TimeProvider.System);

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
            return new WeatherUiService(client, signalR);
        });

        builder.Services.AddScoped<IDisplaySettingService, DisplaySettingService>();
        builder.Services.AddScoped<IWeatherOverrideService, WeatherOverrideService>();

        var host = builder.Build();
        await host.Services.GetRequiredService<IVersionService>().InitializeAsync();
        await host.RunAsync();
    }
}
