using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.Services.Auth;
using FamilyHQ.WebUi.Services.Correlation;

namespace FamilyHQ.WebUi;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        var backendUrl = builder.Configuration["BackendUrl"] ?? "https://localhost:5001";

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

        builder.Services.AddHttpClient<IThemeService, ThemeService>(client =>
        {
            client.BaseAddress = new Uri(backendUrl);
        })
        .AddHttpMessageHandler<CorrelationIdMessageHandler>()
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

        await builder.Build().RunAsync();
    }
}
