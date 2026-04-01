using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public class SettingsApiService : ISettingsApiService
{
    private readonly HttpClient _httpClient;

    public SettingsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LocationSettingDto?> GetLocationAsync()
    {
        var response = await _httpClient.GetAsync("api/settings/location");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationSettingDto>();
    }

    public async Task<LocationSettingDto> SaveLocationAsync(string placeName)
    {
        var response = await _httpClient.PostAsJsonAsync("api/settings/location", new SaveLocationRequest(placeName));

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(body);
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LocationSettingDto>())!;
    }

    public async Task DeleteLocationAsync()
    {
        var response = await _httpClient.DeleteAsync("api/settings/location");
        response.EnsureSuccessStatusCode();
    }

    public async Task<DayThemeDto> GetTodayThemeAsync()
    {
        return (await _httpClient.GetFromJsonAsync<DayThemeDto>("api/daytheme/today"))!;
    }

    public async Task<DisplaySettingDto> GetDisplayAsync()
    {
        return (await _httpClient.GetFromJsonAsync<DisplaySettingDto>("api/settings/display"))!;
    }

    public async Task<DisplaySettingDto> SaveDisplayAsync(DisplaySettingDto dto)
    {
        var response = await _httpClient.PutAsJsonAsync("api/settings/display", dto);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DisplaySettingDto>())!;
    }
}
