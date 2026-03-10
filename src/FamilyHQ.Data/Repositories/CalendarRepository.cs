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
        var currentUserId = _currentUserService.UserId ?? "default_simulator_user";

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
            .AsNoTracking()
            .Where(e => e.CalendarInfoId == calendarInfoId && e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _context.Events
            .AsNoTracking()
            .Include(e => e.CalendarInfo)
            .Where(e => e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.CalendarInfo)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<CalendarEvent?> GetEventByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.CalendarInfo)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<SyncState?> GetSyncStateAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        return await _context.SyncStates
            .FirstOrDefaultAsync(s => s.CalendarInfoId == calendarInfoId, ct);
    }

    public Task AddCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default)
    {
        calendarInfo.UserId = _currentUserService.UserId ?? "default_simulator_user";
        _context.Calendars.Add(calendarInfo);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        _context.Events.Add(calendarEvent);
        return Task.CompletedTask;
    }

    public Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        _context.Events.Update(calendarEvent);
        return Task.CompletedTask;
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken ct = default)
    {
        var calendarEvent = await _context.Events.FindAsync(new object[] { id }, ct);
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
