using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public class VersionService : IVersionService
{
    private const string HealthPath = "api/health";
    private const string UnknownVersion = "0.0.0-unknown";
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IJSRuntime _jsRuntime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VersionService> _logger;

    private bool _updateTriggered;

    public string ClientVersion { get; }
    public string? ServerVersion { get; private set; }

    public event Action? UpdateAvailable;

    public VersionService(
        HttpClient httpClient,
        IJSRuntime jsRuntime,
        TimeProvider timeProvider,
        ILogger<VersionService> logger,
        ISignalRConnectionEvents connectionEvents)
    {
        _httpClient = httpClient;
        _jsRuntime = jsRuntime;
        _timeProvider = timeProvider;
        _logger = logger;

        ClientVersion = ResolveClientVersion();

        connectionEvents.Reconnected += OnReconnected;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var health = await _httpClient.GetFromJsonAsync<HealthResponse>(HealthPath, JsonOptions, ct);
            if (health is not null)
            {
                ServerVersion = health.Version;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Failed to fetch initial server version from {HealthPath}", HealthPath);
        }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (_updateTriggered)
        {
            return;
        }

        try
        {
            var health = await _httpClient.GetFromJsonAsync<HealthResponse>(HealthPath, JsonOptions, ct);
            if (health is null)
            {
                return;
            }

            ServerVersion = health.Version;

            if (string.IsNullOrWhiteSpace(health.Version))
            {
                return;
            }

            if (VersionsMatch(ClientVersion, health.Version))
            {
                return;
            }

            _updateTriggered = true;
            UpdateAvailable?.Invoke();

            await Task.Delay(ReloadDelay, _timeProvider, ct);
            await _jsRuntime.InvokeVoidAsync("location.reload");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Version check failed");
        }
    }

    private void OnReconnected()
    {
        _ = CheckAsync();
    }

    private static bool VersionsMatch(string a, string b)
    {
        return string.Equals(StripBuildMetadata(a), StripBuildMetadata(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    private static string ResolveClientVersion()
    {
        var assembly = typeof(VersionService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? UnknownVersion : assemblyVersion;
    }

    private sealed record HealthResponse(string Status, string? Service, string? Version, DateTimeOffset Timestamp);
}
