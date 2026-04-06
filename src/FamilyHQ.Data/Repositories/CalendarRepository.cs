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

    private string CurrentUserId => _currentUserService.UserId ?? string.Empty;

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return Array.Empty<CalendarInfo>();

        return await _context.Calendars
            .AsNoTracking()
            .Where(c => c.UserId == CurrentUserId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<CalendarInfo?> GetCalendarByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Calendars
            .AsNoTracking()
            .Include(c => c.SyncState)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<CalendarInfo?> GetSharedCalendarAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return null;

        return await _context.Calendars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == CurrentUserId && c.IsShared, ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsByOwnerCalendarAsync(
        Guid calendarInfoId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        return await _context.Events
            .AsNoTracking()
            .Include(e => e.Members)
            .Where(e => e.OwnerCalendarInfoId == calendarInfoId && e.Start < end && e.End > start)
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentUserId))
            return Array.Empty<CalendarEvent>();

        return await _context.Events
            .AsNoTracking()
            .Include(e => e.Members)
            .Where(e => e.Start < end && e.End > start
                     && _context.Calendars.Any(c => c.Id == e.OwnerCalendarInfoId && c.UserId == CurrentUserId))
            .OrderBy(e => e.Start)
            .ToListAsync(ct);
    }

    public async Task<CalendarEvent?> GetEventAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Members)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<CalendarEvent?> GetEventByGoogleEventIdAsync(string googleEventId, CancellationToken ct = default)
    {
        return await _context.Events
            .Include(e => e.Members)
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

        // Delete all events owned by this calendar (members junction rows cascade via EF)
        var ownedEvents = await _context.Events
            .Include(e => e.Members)
            .Where(e => e.OwnerCalendarInfoId == calendarInfoId)
            .ToListAsync(ct);

        _context.Events.RemoveRange(ownedEvents);

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

    public Task UpdateCalendarAsync(CalendarInfo calendarInfo, CancellationToken ct = default)
    {
        _context.Calendars.Update(calendarInfo);
        return Task.CompletedTask;
    }

    public Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Attach member CalendarInfos so EF registers EventMembers join rows correctly.
        // If a CalendarInfo with the same Id is already tracked by this context, use that
        // instance instead of attaching — calling Attach on a second instance with the
        // same key throws InvalidOperationException.
        var trackedMembers = calendarEvent.Members
            .Select(m =>
            {
                var entry = _context.Entry(m);
                if (entry.State != EntityState.Detached)
                    return m;

                var existing = _context.ChangeTracker.Entries<CalendarInfo>()
                    .FirstOrDefault(e => e.Entity.Id == m.Id);
                return existing != null ? existing.Entity : _context.Calendars.Attach(m).Entity;
            })
            .ToList();

        calendarEvent.Members = trackedMembers;
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
        var evt = await _context.Events.FindAsync([id], ct);
        if (evt != null)
            _context.Events.Remove(evt);
    }

    public Task SaveSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        var entry = _context.Entry(syncState);
        if (entry.State == EntityState.Detached)
            _context.SyncStates.Update(syncState);
        return Task.CompletedTask;
    }

    public Task AddSyncStateAsync(SyncState syncState, CancellationToken ct = default)
    {
        _context.SyncStates.Add(syncState);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
