using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.ViewModels;
using FamilyHQ.WebUi.Services.Auth;

namespace FamilyHQ.WebUi.Services;

public class CalendarApiService : ICalendarApiService
{
    private readonly HttpClient _httpClient;
    private readonly IAuthTokenStore _tokenStore;

    public CalendarApiService(HttpClient httpClient, IAuthTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _tokenStore = tokenStore;
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("api/calendars", ct);
        
        // MVP: if we haven't implemented GET /api/calendars on the WebApi yet, return empty list
        if (!response.IsSuccessStatusCode) return Array.Empty<CalendarInfo>();
            
        return await response.Content.ReadFromJsonAsync<List<CalendarInfo>>(cancellationToken: ct) ?? new List<CalendarInfo>();
    }

    public async Task<MonthViewDto?> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/calendars/events?year={year}&month={month}", ct);
        
        if (!response.IsSuccessStatusCode) return null;
            
        return await response.Content.ReadFromJsonAsync<MonthViewDto>(cancellationToken: ct);
    }
    public async Task<CalendarEventViewModel?> CreateEventAsync(Guid calendarId, CreateEventRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"api/calendars/{calendarId}/events", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<CalendarEventViewModel>(cancellationToken: ct);
    }

    public async Task<CalendarEventViewModel?> UpdateEventAsync(Guid calendarId, Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/calendars/{calendarId}/events/{eventId}", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<CalendarEventViewModel>(cancellationToken: ct);
    }

    public async Task<bool> DeleteEventAsync(Guid calendarId, Guid eventId, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/calendars/{calendarId}/events/{eventId}", ct);
        return response.IsSuccessStatusCode;
    }
    
    public async Task SimulateLoginAsync(string? userName = null)
    {
        var request = new { SimulatedUserId = userName };
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (content?.Token != null)
        {
            await _tokenStore.SetTokenAsync(content.Token);
        }
    }
    
    private class LoginResponse
    {
        public string? Token { get; set; }
        public string? UserId { get; set; }
    }
}
