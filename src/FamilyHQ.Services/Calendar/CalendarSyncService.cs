using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarSyncService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    IMemberTagParser memberTagParser,
    ILogger<CalendarSyncService> logger,
    ITokenStore tokenStore,
    ICurrentUserService currentUserService,
    ISyncFailureRepository syncFailureRepository,
    IOutboundWriteHashCache outboundWriteHashCache) : ICalendarSyncService
{
    public async Task<SyncResult> SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        // Capture the current user id ONCE at sync entry. IHttpContextAccessor.HttpContext
        // is AsyncLocal-backed and can become null after the response has been sent or
        // when async continuations resume on a thread without the request's execution
        // context. Resolving userId lazily inside a catch block (after multiple awaits)
        // races against that lifetime; the user could be authoritatively known at sync
        // start but unobservable by the time we want to mark them NeedsReauth. Capturing
        // up front pins the value to the sync's logical scope and eliminates the race.
        var capturedUserId = currentUserService.UserId;
        if (string.IsNullOrEmpty(capturedUserId))
        {
            logger.LogWarning("SyncAllAsync invoked with no current user id; aborting sync.");
            return new SyncResult(0);
        }

        logger.LogInformation("Starting full sync from {Start} to {End} for user {UserId}", startDate, endDate, capturedUserId);

        int changeCount = 0;

        List<CalendarInfo> googleCalendars;
        try
        {
            googleCalendars = (await googleCalendarClient.GetCalendarsAsync(ct)).ToList();
        }
        catch (GoogleReauthRequiredException ex)
        {
            await MarkUserNeedsReauthAsync(capturedUserId, ex, ct);
            throw;
        }
        var localCalendars  = await calendarRepository.GetCalendarsAsync(ct);

        // Remove obsolete local calendars
        var obsolete = localCalendars
            .Where(local => !googleCalendars.Any(g => g.GoogleCalendarId == local.GoogleCalendarId))
            .ToList();

        foreach (var cal in obsolete)
        {
            logger.LogInformation("Removing obsolete calendar {CalendarId}", cal.Id);
            await calendarRepository.RemoveCalendarAsync(cal.Id, ct);
        }

        if (obsolete.Count > 0)
            changeCount += await calendarRepository.SaveChangesAsync(ct);

        // Pass 1: ensure every Google calendar exists in the local DB before syncing
        // any events.  Multi-member events on the shared calendar reference member
        // calendars by display name; if those member calendars are not yet present
        // when the shared calendar is synced first, member-tag parsing inside
        // SyncCoreAsync returns an empty member list and the event is persisted with
        // no junction rows.
        //
        // New calendars are assigned a sequential DisplayOrder starting from the
        // current maximum so Day/Agenda column order is deterministic (matches the
        // order Google returns them) and the user-facing reorder feature still has
        // unique sort keys to work with.
        var nextOrder = localCalendars.Count == 0 ? 0 : localCalendars.Max(c => c.DisplayOrder) + 1;
        var calendarIdsToSync = new List<Guid>(googleCalendars.Count);
        foreach (var googleCal in googleCalendars)
        {
            var localCal = localCalendars.FirstOrDefault(c => c.GoogleCalendarId == googleCal.GoogleCalendarId);
            if (localCal == null)
            {
                googleCal.DisplayOrder = nextOrder++;
                await calendarRepository.AddCalendarAsync(googleCal, ct);
                changeCount += await calendarRepository.SaveChangesAsync(ct);
                localCal = googleCal;
            }
            calendarIdsToSync.Add(localCal.Id);
        }

        // Pass 2: sync events for every calendar.  By the time SyncCoreAsync calls
        // GetCalendarsAsync (line ~83) the local DB contains all calendars, so
        // member-tag parsing resolves correctly regardless of iteration order.
        //
        // Auto-designation of the shared calendar is deliberately deferred until
        // AFTER this loop.  SyncCoreAsync's memberless-event fallback adds the
        // owning calendar to the Members list only when `!calendar.IsShared`.  If
        // we designated the first calendar as shared before syncing its events,
        // every event with no member tags would be persisted with Members=[] and
        // then get stranded off the dashboard once the user picks a different
        // shared calendar in settings.
        // FHQ-25 (WS1): a reauth/permission failure on any calendar aborts the loop
        // because all of this user's calendars share the same OAuth token — once Google
        // rejects it once, every remaining calendar would reject it too. Per-event
        // resilience (so one malformed event in one calendar doesn't abort the rest of
        // that calendar's events) is FHQ-26 / WS2.
        try
        {
            foreach (var calendarId in calendarIdsToSync)
            {
                changeCount += (await SyncAsync(calendarId, startDate, endDate, ct)).ChangedCount;
            }
        }
        catch (GoogleReauthRequiredException ex)
        {
            await MarkUserNeedsReauthAsync(capturedUserId, ex, ct);
            throw;
        }

        // First-login default: if the user has more than one calendar but none is
        // designated as shared, auto-designate the first calendar as shared.  This
        // covers the initial sync after sign-up where the user has not yet picked
        // a shared calendar via settings.  Single-calendar accounts are left alone
        // — there is no "shared" concept for a user with only one calendar.
        //
        // Pass 2 above FindAsync-ed tracked CalendarInfo instances for every
        // calendar that had events synced.  Re-querying via GetCalendarsAsync here
        // would return AsNoTracking duplicates and `_context.Calendars.Update(...)`
        // would collide with the already-tracked instance.  MarkCalendarAsSharedAsync
        // mutates the tracked entity in place to avoid that conflict.
        var calendarsAfterSync = (await calendarRepository.GetCalendarsAsync(ct)).ToList();
        if (calendarsAfterSync.Count > 1 && !calendarsAfterSync.Any(c => c.IsShared))
        {
            var firstCalendarId = calendarIdsToSync.First();
            var firstCalendarName = calendarsAfterSync.First(c => c.Id == firstCalendarId).DisplayName;
            await calendarRepository.MarkCalendarAsSharedAsync(firstCalendarId, ct);
            changeCount += await calendarRepository.SaveChangesAsync(ct);
            logger.LogInformation(
                "Auto-designated {CalendarName} as the shared calendar (no prior designation).",
                firstCalendarName);
        }

        logger.LogInformation("Finished syncing all calendars.");
        return new SyncResult(changeCount);
    }

    public async Task<SyncResult> SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
        => new SyncResult(await SyncCoreAsync(calendarInfoId, startDate, endDate, isRetry: false, ct));

    private async Task<int> SyncCoreAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, bool isRetry, CancellationToken ct)
    {
        var calendar = await calendarRepository.GetCalendarByIdAsync(calendarInfoId, ct);
        if (calendar == null)
        {
            logger.LogWarning("Calendar {CalendarId} not found. Skipping sync.", calendarInfoId);
            return 0;
        }

        bool isNewSyncState = false;
        var syncState = await calendarRepository.GetSyncStateAsync(calendarInfoId, ct);
        if (syncState == null)
        {
            syncState = new SyncState { CalendarInfoId = calendarInfoId };
            isNewSyncState = true;
        }

        bool isFullSync = string.IsNullOrEmpty(syncState.SyncToken);

        logger.LogInformation("Syncing {CalendarName}. FullSync={IsFullSync}", calendar.DisplayName, isFullSync);

        int changeCount = 0;

        try
        {
            var (fetchedEvents, nextSyncToken) = await googleCalendarClient.GetEventsAsync(
                calendar.GoogleCalendarId,
                isFullSync ? startDate : null,
                isFullSync ? endDate : null,
                syncState.SyncToken,
                ct);

            // Materialise once: the sequence is enumerated several times below (pass-2 resolution,
            // tombstone diff, the persistence loop, the final count) and the loop mutates each
            // instance's RecurrenceRule — a lazy sequence would re-execute and lose those writes.
            var events = fetchedEvents as IReadOnlyList<CalendarEvent> ?? fetchedEvents.ToList();

            var allLocalCalendars = await calendarRepository.GetCalendarsAsync(ct);
            // FHQ-46: resolve member tags against ALL calendars, not just non-shared ones. An explicit
            // "[members: ...]" tag is an authoritative membership declaration, so a calendar named in it
            // must resolve even when it is currently IsShared. Otherwise the transient first-login window
            // — where auto-designation marks the first calendar shared before the user/E2E picks the real
            // shared calendar — silently DROPS that member on every re-sync (membership is re-derived and
            // overwritten below), making an event's calendar membership flap (the chip-remove/agenda flake).
            // The app never writes the shared calendar into a tag (NormaliseDescription strips it), so
            // including shared calendars here only ever rescues a legitimately-tagged member; it never
            // invents one. The memberless-event fallback below still excludes the shared container calendar.
            var knownMemberNames  = allLocalCalendars.Select(c => c.DisplayName).ToList();
            var calendarByName    = allLocalCalendars
                .ToDictionary(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);

            // Pass 2 (recurrence): resolve an RRULE for every recurring series referenced by the
            // pass-1 instances and cache it for this sync run, so each unknown master is fetched
            // at most once. Cancelled tombstones and self-echoes are excluded (see the resolver).
            var rruleCache = await ResolveSeriesRecurrenceRulesAsync(calendar.GoogleCalendarId, events, ct);

            if (isFullSync)
            {
                // Tombstone events no longer present in Google
                var existingEvents   = await calendarRepository.GetEventsByOwnerCalendarAsync(calendarInfoId, startDate, endDate, ct);
                var fetchedGoogleIds = events.Select(e => e.GoogleEventId).ToHashSet();
                var obsoleteList     = existingEvents.Where(e => !fetchedGoogleIds.Contains(e.GoogleEventId)).ToList();

                foreach (var obsoleteEvt in obsoleteList)
                    await calendarRepository.DeleteEventAsync(obsoleteEvt.Id, ct);

                if (obsoleteList.Count > 0)
                    changeCount += await calendarRepository.SaveChangesAsync(ct);
            }

            foreach (var evt in events)
            {
                // Self-echo guard (FHQ-30): skip events that echo our own outbound writes.
                // ContentHash is populated by GoogleCalendarClient from extendedProperties.private["content-hash"].
                // Null hash means a manually-edited event, a delete tombstone, or a legacy event — always process.
                if (IsSelfEcho(evt))
                {
                    logger.LogInformation(
                        "Self-echo skipped for event {EventId} on calendar {CalendarInfoId} (hash {Hash}).",
                        evt.GoogleEventId, calendarInfoId, evt.ContentHash);
                    continue;
                }

                // Stamp the resolved RRULE (pass 2) onto recurring instances before persistence.
                // A series whose master could not be fetched this run is left with RecurrenceRule
                // null so the next sync retries it.
                if (evt.GoogleRecurringEventId is not null
                    && rruleCache.TryGetValue(evt.GoogleRecurringEventId, out var resolvedRrule))
                {
                    evt.RecurrenceRule = resolvedRrule;
                }

                // The entity actually written to the change tracker for this event.
                // If a downstream SaveChangesAsync throws (e.g. Postgres rejects the
                // value as too long), we need to detach this specific entity so the
                // failure does not poison subsequent per-event saves.
                CalendarEvent? touched = null;
                try
                {
                    if (evt.Title == "CANCELLED_TOMBSTONE")
                    {
                        var tracked = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                        if (tracked != null)
                        {
                            touched = tracked;
                            await calendarRepository.DeleteEventAsync(tracked.Id, ct);
                            changeCount += await calendarRepository.SaveChangesAsync(ct);
                        }
                        continue;
                    }

                    // Derive members from description
                    var parsedNames   = memberTagParser.ParseMembers(evt.Description, knownMemberNames);
                    var parsedMembers = parsedNames
                        .Where(n => calendarByName.ContainsKey(n))
                        .Select(n => calendarByName[n])
                        .ToList();

                    // If no members parsed and this is an individual calendar, default to owning calendar's member
                    if (parsedMembers.Count == 0 && !calendar.IsShared)
                        parsedMembers.Add(calendar);

                    var existing = await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);
                    if (existing != null)
                    {
                        touched = existing;
                        existing.Title                  = evt.Title;
                        existing.Start                  = evt.Start;
                        existing.End                    = evt.End;
                        existing.IsAllDay               = evt.IsAllDay;
                        existing.Location               = evt.Location;
                        existing.Description            = evt.Description;
                        existing.Members                = parsedMembers;
                        existing.GoogleRecurringEventId = evt.GoogleRecurringEventId;
                        existing.OriginalStartTime      = evt.OriginalStartTime;
                        // Preserve an already-stored RRULE if pass 2 could not resolve one this run
                        // (transient master-fetch failure must not blank out a known rule).
                        existing.RecurrenceRule         = evt.RecurrenceRule ?? existing.RecurrenceRule;
                        await calendarRepository.UpdateEventAsync(existing, ct);
                    }
                    else
                    {
                        touched = evt;
                        evt.OwnerCalendarInfoId = calendar.Id;
                        evt.Members             = parsedMembers;
                        await calendarRepository.AddEventAsync(evt, ct);
                    }

                    // Commit each event individually. A constraint violation
                    // (e.g. Title longer than the column max length) throws here
                    // and is handled by the catch below; legitimate events that
                    // were committed earlier in the loop are not rolled back.
                    changeCount += await calendarRepository.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is not GoogleReauthRequiredException and not OperationCanceledException)
                {
                    if (touched != null)
                    {
                        try
                        {
                            await calendarRepository.DetachEventAsync(touched, ct);
                        }
                        catch (Exception detachEx)
                        {
                            logger.LogWarning(
                                detachEx,
                                "Failed to detach {GoogleEventId} after sync failure; subsequent per-event saves may be affected.",
                                evt.GoogleEventId);
                        }
                    }

                    await RecordEventFailureAsync(evt, calendar.Id, ex, ct);

                    try
                    {
                        await calendarRepository.SaveChangesAsync(ct);
                    }
                    catch (Exception saveEx)
                    {
                        logger.LogError(
                            saveEx,
                            "Failed to persist SyncEventFailure for {GoogleEventId}; failure will not appear in diagnostics.",
                            evt.GoogleEventId);
                    }
                }
            }

            syncState.SyncToken    = nextSyncToken;
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd   = endDate;
            }

            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);

            // Bookkeeping only — excluded from the material change count (FHQ-44).
            await calendarRepository.SaveChangesAsync(ct);
            logger.LogInformation("Synced {Count} events for {CalendarName}.", events.Count(), calendar.DisplayName);
            return changeCount;
        }
        catch (InvalidOperationException ex) when (!isRetry && ex.Message.Contains("no longer valid"))
        {
            logger.LogWarning("Sync token expired for {CalendarName}. Restarting full sync.", calendar.DisplayName);
            syncState.SyncToken = null;
            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);
            // Bookkeeping only — excluded from the material change count (FHQ-44).
            await calendarRepository.SaveChangesAsync(ct);
            return await SyncCoreAsync(calendarInfoId, startDate, endDate, isRetry: true, ct);
        }
    }

    /// <summary>
    /// FHQ-30 self-echo guard: true when this inbound event echoes one of our own recent
    /// outbound writes (matching GoogleEventId + content-hash). A null/empty hash means a
    /// manually-edited event, a tombstone, or a legacy event — never an echo.
    /// </summary>
    private bool IsSelfEcho(CalendarEvent evt)
        => !string.IsNullOrEmpty(evt.ContentHash)
           && outboundWriteHashCache.WasRecentlyWritten(evt.GoogleEventId, evt.ContentHash);

    /// <summary>
    /// Pass 2 of recurring ingestion. Builds a per-run series-id → RRULE cache from the
    /// pass-1 instances: series whose RRULE is already stored locally skip the API entirely,
    /// and each remaining unknown master is fetched exactly once. A transient master-fetch
    /// failure (null or non-reauth exception) leaves that series out of the cache so its
    /// instances persist with a null RRULE and the next sync retries; a reauth failure
    /// propagates so the user is prompted to reconnect.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveSeriesRecurrenceRulesAsync(
        string googleCalendarId, IEnumerable<CalendarEvent> events, CancellationToken ct)
    {
        // Cancelled tombstones reuse the recurring id but are being deleted, so they need no RRULE.
        // Self-echoes (FHQ-30) are short-circuited in the persistence loop and never stored, so
        // they must not trigger a wasted master fetch either.
        var seriesIds = events
            .Where(e => e.GoogleRecurringEventId is not null
                     && e.Title != "CANCELLED_TOMBSTONE"
                     && !IsSelfEcho(e))
            .Select(e => e.GoogleRecurringEventId!)
            .Distinct()
            .ToList();

        if (seriesIds.Count == 0)
            return new Dictionary<string, string>();

        var cache = new Dictionary<string, string>(
            await calendarRepository.GetStoredRecurrenceRulesAsync(seriesIds, ct));

        foreach (var seriesId in seriesIds.Where(id => !cache.ContainsKey(id)))
        {
            try
            {
                var master = await googleCalendarClient.GetSeriesMasterAsync(googleCalendarId, seriesId, ct);
                if (master is not null)
                    cache[seriesId] = master.Rrule;
                else
                    logger.LogWarning(
                        "Series master {SeriesId} on calendar {GoogleCalendarId} returned no RRULE; instances persisted without one and will retry next sync.",
                        seriesId, googleCalendarId);
            }
            catch (Exception ex) when (ex is not GoogleReauthRequiredException and not OperationCanceledException)
            {
                // Transient API failure: degrade gracefully, retry the series next sync.
                logger.LogWarning(
                    ex,
                    "Failed to fetch series master {SeriesId} on calendar {GoogleCalendarId}; instances persisted without an RRULE and will retry next sync.",
                    seriesId, googleCalendarId);
            }
        }

        return cache;
    }

    private async Task RecordEventFailureAsync(CalendarEvent evt, Guid calendarInfoId, Exception ex, CancellationToken ct)
    {
        var userId = currentUserService.UserId ?? string.Empty;
        logger.LogError(
            ex,
            "Sync failed for event {GoogleEventId} on calendar {CalendarInfoId} (user {UserId}): {ExceptionType} — {Message}",
            evt.GoogleEventId,
            calendarInfoId,
            userId,
            ex.GetType().Name,
            ex.Message);

        // Column widths are enforced by EF/Postgres. EF Core constraint-violation
        // messages can easily exceed 512 chars, so truncating defensively here
        // prevents the failure-write itself from throwing inside the catch and
        // losing the diagnostic record entirely.
        var failure = new SyncEventFailure
        {
            UserId = userId,
            CalendarInfoId = calendarInfoId,
            GoogleEventId = evt.GoogleEventId,
            EventTitle = Truncate(evt.Title, 256),
            FailureReason = Truncate(ex.Message, 512) ?? string.Empty,
            ExceptionType = Truncate(ex.GetType().FullName ?? ex.GetType().Name, 256) ?? "Exception",
            FailedAt = DateTimeOffset.UtcNow,
            Resolved = false
        };

        await syncFailureRepository.AddAsync(failure, ct);
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null) return null;
        return value.Length <= max ? value : value[..max];
    }

    private Task MarkUserNeedsReauthAsync(string capturedUserId, GoogleReauthRequiredException ex, CancellationToken ct)
        => tokenStore.MarkNeedsReauthAsync(capturedUserId, ex.ErrorDescription, ct);
}
