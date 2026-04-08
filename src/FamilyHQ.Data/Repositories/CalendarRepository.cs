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

        // Secondary sort on Id ensures a deterministic order when multiple
        // calendars share the same DisplayOrder (e.g. freshly synced accounts
        // where every calendar defaults to 0).  Without it, Postgres can return
        // tied rows in heap order, which changes whenever a row is UPDATEd.
        return await _context.Calendars
            .AsNoTracking()
            .Where(c => c.UserId == CurrentUserId)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Id)
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

    public async Task MarkCalendarAsSharedAsync(Guid calendarInfoId, CancellationToken ct = default)
    {
        // FindAsync hits the identity map first — if the calendar is already tracked
        // from an earlier Add/FindAsync in this scope (sync service Pass 1 attaches
        // every calendar; AddEventAsync/UpdateEventAsync re-FindAsync tracked members)
        // we reuse that tracked instance.  Mutating it in-place lets EF's DetectChanges
        // schedule a simple IsShared update without colliding with an AsNoTracking
        // duplicate already in the tracker.
        var tracked = await _context.Calendars.FindAsync([calendarInfoId], ct);
        if (tracked != null)
            tracked.IsShared = true;
    }

    public async Task AddEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Resolve member CalendarInfos to properly-tracked instances via FindAsync.
        // FindAsync checks the identity map first (no DB round-trip if already tracked),
        // then falls back to a DB query.  This avoids the tracking conflicts that arise
        // from Attach() when a related entity (e.g. CalendarInfo.SyncState) is already
        // tracked under a different object reference, and it avoids partial-DetectChanges
        // side-effects that occur when using Entry() inside a resolution loop.
        var memberIds = calendarEvent.Members.Select(m => m.Id).ToList();

        var resolvedMembers = new List<CalendarInfo>(memberIds.Count);
        foreach (var id in memberIds)
        {
            var tracked = await _context.Calendars.FindAsync([id], ct);
            if (tracked != null)
                resolvedMembers.Add(tracked);
        }

        // Disable AutoDetectChanges around the Add call.  In the sync loop multiple
        // events are added before SaveChanges; with AutoDetect on, _context.Events.Add
        // triggers DetectChanges across the whole tracker for each event, which can
        // process AsNoTracking instances still referenced by other events' Members
        // navigations and produce incomplete junction-row state.  A single DetectChanges
        // at SaveChanges time, with all events fully resolved, is the safe path.
        var wasAutoDetect = _context.ChangeTracker.AutoDetectChangesEnabled;
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            calendarEvent.Members = resolvedMembers;
            _context.Events.Add(calendarEvent);
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = wasAutoDetect;
        }
    }

    public async Task UpdateEventAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        // Resolve member CalendarInfos to tracked instances via FindAsync (identity-map
        // first, DB query only if not cached).  This is safe whether the caller has set
        // a plain List of AsNoTracking instances or the original EF-managed collection.
        var memberIds = calendarEvent.Members.Select(m => m.Id).ToList();

        var resolvedMembers = new List<CalendarInfo>(memberIds.Count);
        foreach (var id in memberIds)
        {
            var tracked = await _context.Calendars.FindAsync([id], ct);
            if (tracked != null)
                resolvedMembers.Add(tracked);
        }

        // Disable AutoDetectChanges while we resolve the entry and set CurrentValue.
        // At this point calendarEvent.Members may still hold the AsNoTracking instances
        // assigned by the caller (e.g. CalendarEventService.SetMembersAsync).  If
        // _context.Entry(calendarEvent) is called with AutoDetectChanges enabled, EF
        // runs DetectChanges immediately and processes the stale AsNoTracking collection
        // against the relationship snapshot.  Setting CurrentValue afterwards does not
        // reliably override the pending junction-row operations that DetectChanges has
        // already scheduled, which can silently drop newly-added member relationships.
        var wasAutoDetect = _context.ChangeTracker.AutoDetectChangesEnabled;
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var entry = _context.Entry(calendarEvent);
            if (entry.State != EntityState.Detached)
            {
                // Entity is already tracked (loaded via GetEventAsync / GetEventByGoogleEventIdAsync).
                // Use NavigationEntry.CurrentValue to update the skip navigation so EF can
                // correctly diff the relationship snapshot against the new collection at
                // SaveChanges time and generate the right INSERT/DELETE on EventMembers.
                entry.Collection(e => e.Members).CurrentValue = resolvedMembers;
            }
            else
            {
                calendarEvent.Members = resolvedMembers;
                _context.Events.Update(calendarEvent);
            }
        }
        finally
        {
            _context.ChangeTracker.AutoDetectChangesEnabled = wasAutoDetect;
        }
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
