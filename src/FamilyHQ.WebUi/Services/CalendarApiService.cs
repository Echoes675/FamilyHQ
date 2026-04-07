using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Services;

public class CalendarApiService(HttpClient httpClient) : ICalendarApiService
{
    public async Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync("api/calendars", ct);

        // Intentional: return empty list on failure so the calendar selector degrades gracefully
        // rather than crashing the entire page. Other methods throw to surface hard errors.
        if (!response.IsSuccessStatusCode) return Array.Empty<CalendarSummaryViewModel>();

        var dtos = await response.Content.ReadFromJsonAsync<List<EventCalendarDto>>(cancellationToken: ct)
                   ?? new List<EventCalendarDto>();

        return dtos.Select(c => new CalendarSummaryViewModel(c.Id, c.DisplayName, c.Color, c.IsShared, c.IsVisible)).ToList();
    }

    public async Task UpdateCalendarSettingsAsync(Guid calendarId, bool isVisible, bool isShared, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/calendars/{calendarId}/settings",
            new { isVisible, isShared }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SaveCalendarOrderAsync(Dictionary<Guid, int> order, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync("api/calendars/order", new { order }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"api/calendars/events?year={year}&month={month}", ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<MonthViewDto>(cancellationToken: ct)
                  ?? new MonthViewDto();

        var expandedDays = new Dictionary<string, List<CalendarEventViewModel>>();

        foreach (var (dateKey, eventDtos) in dto.Days)
        {
            var vms = new List<CalendarEventViewModel>();

            foreach (var evtDto in eventDtos)
            {
                var allCalendars = evtDto.Members
                    .Select(c => new CalendarSummaryViewModel(c.Id, c.DisplayName, c.Color, c.IsShared))
                    .ToList();

                // One ViewModel per member calendar — grid expansion happens here
                foreach (var cal in evtDto.Members)
                {
                    vms.Add(new CalendarEventViewModel(
                        evtDto.Id,
                        evtDto.Title,
                        evtDto.Start,
                        evtDto.End,
                        evtDto.IsAllDay,
                        evtDto.Location,
                        evtDto.Description,
                        cal.Id,
                        cal.DisplayName,
                        cal.Color,
                        allCalendars));
                }
            }

            expandedDays[dateKey] = vms;
        }

        return new MonthViewModel(expandedDays);
    }

    public async Task<CalendarEventViewModel> CreateEventAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/events", request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<CalendarEventDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("API returned empty response for CreateEventAsync.");

        return MapToViewModel(dto);
    }

    public async Task<CalendarEventViewModel> UpdateEventAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync($"api/events/{eventId}", request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<CalendarEventDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("API returned empty response for UpdateEventAsync.");

        return MapToViewModel(dto);
    }

    public async Task DeleteEventAsync(Guid eventId, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"api/events/{eventId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CalendarEventViewModel> SetEventMembersAsync(
        Guid eventId, IReadOnlyList<Guid> memberCalendarInfoIds, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/events/{eventId}/members",
            new { memberCalendarInfoIds }, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<CalendarEventDto>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Empty response from SetEventMembersAsync.");
        return MapToViewModel(dto);
    }

    private static CalendarEventViewModel MapToViewModel(CalendarEventDto dto)
    {
        var allCalendars = dto.Members
            .Select(c => new CalendarSummaryViewModel(c.Id, c.DisplayName, c.Color, c.IsShared))
            .ToList();

        var primary = dto.Members.FirstOrDefault();

        return new CalendarEventViewModel(
            dto.Id,
            dto.Title,
            dto.Start,
            dto.End,
            dto.IsAllDay,
            dto.Location,
            dto.Description,
            primary?.Id ?? Guid.Empty,
            primary?.DisplayName ?? string.Empty,
            primary?.Color,
            allCalendars);
    }
}
