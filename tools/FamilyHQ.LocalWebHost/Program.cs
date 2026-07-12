// Dev-only static host (FHQ-150): serves a published Blazor WASM wwwroot over Kestrel with the
// dev cert, without the DevServer's on-the-fly response compression. UseBlazorFrameworkFiles
// serves the pre-compressed .br/.gz assets directly (content negotiation), so no runtime
// Deflater is created — sidestepping the ZLibException that crashes the DevServer under E2E
// load on .NET 10.0.9 / Windows. Point it at the published wwwroot via WEBUI_WWWROOT and the
// bind address via ASPNETCORE_URLS.
var wwwroot = Environment.GetEnvironmentVariable("WEBUI_WWWROOT")
    ?? throw new InvalidOperationException("WEBUI_WWWROOT (path to the published WebUi wwwroot) must be set.");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = wwwroot });
var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
