using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FamilyHQ.WebUi.Services;

namespace FamilyHQ.WebUi;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        var backendUrl = builder.Configuration["BackendUrl"] ?? "https://localhost:5001";

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(backendUrl) });
        
        builder.Services.AddScoped<ICalendarApiService, CalendarApiService>();
        
        builder.Services.AddSingleton(sp => new SignalRService(backendUrl));

        await builder.Build().RunAsync();
    }
}
