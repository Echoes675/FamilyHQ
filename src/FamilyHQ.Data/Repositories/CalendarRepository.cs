using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class CalendarRepository : ICalendarRepository
{
    private readonly FamilyHqDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CalendarRepository(FamilyHqDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        
        // Return empty list if no user is authenticated (safer than using fallback)
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Array.Empty<CalendarInfo>();
        }

        return await _context.Calendars
            .AsNoTracking()
            .Where(c => c.UserId == currentUserId)
            .ToListAsync(ct);
    }

    public async Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Calendars
            .Include(c => c.SyncState)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(Guid calendarInfoId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Calendars)
            .Where(e => e.Calendars.Any(c => c.Id == calendarInfoId) && e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(currentUserId))
            return Array.Empty<CalendarEvent>();

        return await _context.Events
            .AsNoTracking()
            .Include(e => e.Calendars)
            .Where(e => e.Start < end && e.End > start
                        && e.Calendars.Any(c => c.UserId == currentUserId))
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Calendars)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<CalendarEvent?> GetEventByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Calendars)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<CalendarEvent?> GetEventByGoogleEventIdAsync(string googleEventId, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Calendars)
            .FirstOrDefaultAsync(e => e.GoogleEventId == googleEventId, ct);
    }

    public async Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        return await _context.SyncStates
            .FirstOrDefaultAsync(s => s.CalendarInfoId == calendarInfoId, ct);
    }

    public async Task RemoveCalendarAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        var calendar = await _context.Calendars
            .Include(c => c.SyncState)
            .FirstOrDefaultAsync(c => c.Id == calendarInfoId, ct);

        if (calendar == null) return;

        // Unlink this calendar from all events; delete events that have no remaining links
        var linkedEvents = await _context.Events
            .Include(e => e.Calendars)
            .Where(e => e.Calendars.Any(c => c.Id == calendarInfoId))
            .ToListAsync(ct);

        foreach (var evt in linkedEvents)
        {
            var link = evt.Calendars.FirstOrDefault(c => c.Id == calendarInfoId);
            if (link != null) evt.Calendars.Remove(link);
            if (!evt.Calendars.Any())
                _context.Events.Remove(evt);
        }

        if (calendar.SyncState != null)
            _context.SyncStates.Remove(calendar.SyncState);

        _context.Calendars.Remove(calendar);
    }

    public Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default)
    {
        calendarInfo.UserId = _currentUserService.UserId
            ?? throw new InvalidOperationException("Cannot add calendar: no authenticated user.");
        _context.Calendars.Add(calendarInfo);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Snapshot the calendars, clear the collection, then add the event first so it
        // enters the change tracker in Added state. After that, add each calendar back
        // via the tracked entity's skip navigation so EF Core registers every join row
        // individually — the same pattern used by AddCalendarAsync (which is known to
        // work correctly for multi-calendar events).
        var calendarsToLink = calendarEvent.Calendars.ToList();
        calendarEvent.Calendars.Clear();

        _context.Events.Add(calendarEvent);

        foreach (var cal in calendarsToLink)
        {
            var tracked = _context.Entry(cal).State == EntityState.Detached
                ? _context.Calendars.Attach(cal).Entity
                : cal;
            calendarEvent.Calendars.Add(tracked);
        }

        return Task.CompletedTask;
    }

    public Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        _context.Events.Update(calendarEvent);
        return Task.CompletedTask;
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken ct = default)
    {
        var calendarEvent = await _context.Events.FindAsync([id], ct);
        if (calendarEvent != null)
        {
            _context.Events.Remove(calendarEvent);
        }
    }

    public Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        var entry = _context.Entry(syncState);
        if (entry.State == EntityState.Detached)
        {
            _context.SyncStates.Update(syncState);
        }
        return Task.CompletedTask;
    }

    public Task AddSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        _context.SyncStates.Add(syncState);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
