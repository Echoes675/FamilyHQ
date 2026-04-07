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
        // Add to the context first so EF tracks the entity (State = Added).
        // Then add resolved member instances to the EF-managed collection so that
        // EF's skip-navigation change tracker correctly generates the EventMembers
        // junction rows on SaveChanges.
        var membersToLink = calendarEvent.Members.ToList();
        calendarEvent.Members = [];
        _context.Events.Add(calendarEvent);

        foreach (var m in membersToLink)
        {
            var mEntry = _context.Entry(m);
            CalendarInfo tracked;
            if (mEntry.State != EntityState.Detached)
            {
                tracked = m;
            }
            else
            {
                var existing = _context.ChangeTracker.Entries<CalendarInfo>()
                    .FirstOrDefault(e => e.Entity.Id == m.Id);
                tracked = existing != null ? existing.Entity : _context.Calendars.Attach(m).Entity;
            }

            calendarEvent.Members.Add(tracked);
        }

        return Task.CompletedTask;
    }

    public Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Resolve member instances: use already-tracked instances where available,
        // otherwise attach the AsNoTracking instances from the caller.
        var resolvedMembers = calendarEvent.Members
            .Select(m =>
            {
                var mEntry = _context.Entry(m);
                if (mEntry.State != EntityState.Detached) return m;
                var existing = _context.ChangeTracker.Entries<CalendarInfo>()
                    .FirstOrDefault(e => e.Entity.Id == m.Id);
                return existing != null ? existing.Entity : _context.Calendars.Attach(m).Entity;
            })
            .ToList();

        var entry = _context.Entry(calendarEvent);
        if (entry.State != EntityState.Detached)
        {
            // Entity is already tracked (loaded via GetEventAsync / GetEventByGoogleEventIdAsync).
            // Use the NavigationEntry API to update the collection so EF's skip-navigation
            // change detection correctly computes which junction rows to add/remove.
            // Direct assignment (entity.Members = list) replaces the EF proxy collection with
            // a plain List, which breaks change detection for many-to-many skip navigations.
            entry.Collection(e => e.Members).CurrentValue = resolvedMembers;
        }
        else
        {
            calendarEvent.Members = resolvedMembers;
            _context.Events.Update(calendarEvent);
        }

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
