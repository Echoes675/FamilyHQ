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

        // The prune's candidate calendars, read BEFORE the fetch rather than after it — the same
        // ordering, for the same reason, as CalendarEventService.ReconcileWindowAsync and the
        // tombstone diff in SyncCoreAsync below. Only a calendar that was already stored when the
        // fetch was taken can be judged by that fetch: a calendar inserted afterwards is missing
        // from it for that reason alone. Pass 1 below is the only caller of AddCalendarAsync, and
        // two of these passes can run at once — SyncController.TriggerSync runs SyncAllAsync inline
        // on the HTTP request while CalendarSyncWorker runs it from the job queue, with nothing
        // serialising the two — so the row this prune would judge unseen is one a concurrent sync
        // has just inserted, for a calendar Google does list. Removing it takes every event that
        // calendar owns and its SyncState with it (see RemoveCalendarAsync), and what a later sync
        // brings back is a new row: visibility and shared designation fall back to their defaults
        // and the display order is reassigned to the end, so the ones the family chose are gone
        // either way. The two failure modes are not symmetrical, which is the whole argument for
        // the ordering — a calendar this early read misses is one the next sync prunes anyway if
        // Google really has stopped listing it.
        var calendarsBeforeFetch = await calendarRepository.GetCalendarsAsync(ct);

        GoogleCalendarFetch calendarFetch;
        List<CalendarInfo> googleCalendars;
        try
        {
            calendarFetch = await googleCalendarClient.GetCalendarsAsync(ct);
            googleCalendars = calendarFetch.Calendars.ToList();
        }
        catch (GoogleReauthRequiredException ex)
        {
            await MarkUserNeedsReauthAsync(capturedUserId, ex, ct);
            throw;
        }
        // Deliberately a SECOND read, after the fetch, and NOT the pre-fetch snapshot above: pass 1
        // decides whether to call AddCalendarAsync, and the unique index on
        // (GoogleCalendarId, UserId) rejects an insert for a calendar a concurrent sync has already
        // added — failing the whole account's sync. Pass 1 needs the latest view precisely because
        // the prune needs the earlier one. Do not collapse these two reads into one.
        var localCalendars  = await calendarRepository.GetCalendarsAsync(ct);

        // Remove obsolete local calendars — but only against an answer that could have named them.
        // RemoveCalendarAsync takes every event the calendar owns and its SyncState with it, so two
        // answers are refused outright rather than acted on partially.
        List<CalendarInfo> obsolete = [];
        if (!calendarFetch.IsComplete)
        {
            // GetCalendarsAsync reports this itself: false when it took its calendarList page cap
            // with a page token still outstanding, or when a page's body yielded no readable
            // `items`. Either way it holds fewer calendars than Google listed and cannot say which
            // ones are missing, so no local calendar is judgeable against it.
            logger.LogWarning(
                "Full sync fetched {CalendarCount} Google calendar(s) but reported the fetch incomplete, so it " +
                "is no statement of what Google holds and none of the {CandidateCount} local calendar(s) was removed.",
                googleCalendars.Count, calendarsBeforeFetch.Count);
        }
        else if (googleCalendars.Count == 0)
        {
            // A complete answer naming NO calendars is refused too, which is this guard's one
            // departure from acting on what Google says. Every Google account has a primary calendar
            // and the API lists it in calendarList, so an account with none is a state this
            // application cannot legitimately reach — whereas acting on it removes every calendar
            // the family has, every event on them, and their SyncState, leaving the kiosk blank. The
            // cost of refusing is an orphaned calendar row surviving a case that should not occur.
            logger.LogWarning(
                "Full sync fetched a complete but empty Google calendar list, which an account holding a primary " +
                "calendar cannot produce, so none of the {CandidateCount} local calendar(s) was removed.",
                calendarsBeforeFetch.Count);
        }
        else
        {
            obsolete = calendarsBeforeFetch
                .Where(local => !googleCalendars.Any(g => g.GoogleCalendarId == local.GoogleCalendarId))
                .ToList();
        }

        foreach (var cal in obsolete)
        {
            // FHQ-166: {CalendarInfoId}, not {CalendarId}. The value has always been FamilyHQ's own
            // Guid and is perfectly safe, but SKILL.md now lists {CalendarId} as a PII placeholder —
            // a Seq user following the rule would distrust a field that is fine.
            logger.LogInformation("Removing obsolete calendar {CalendarInfoId}", cal.Id);
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
            else
            {
                // FHQ-211: a name or colour adopted from Google IS a material change — the dashboard
                // renders both, and the kiosk only refetches the calendar list when the sync reports
                // one. The zone and reminders it also adopts stay bookkeeping.
                changeCount += await RefreshCalendarDefaultsAsync(localCal, googleCal, ct);
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
        //
        // calendarIdsToSync can be EMPTY while local calendars survive, which is new: the prune
        // above refuses to act on a calendar list that named nothing, so the local rows stay while
        // this sync has processed no calendar to designate. First() on the empty list would throw
        // InvalidOperationException out of the whole sync; there is simply nothing to designate.
        var calendarsAfterSync = (await calendarRepository.GetCalendarsAsync(ct)).ToList();
        if (calendarIdsToSync.Count > 0 && calendarsAfterSync.Count > 1 && !calendarsAfterSync.Any(c => c.IsShared))
        {
            var firstCalendarId = calendarIdsToSync.First();
            await calendarRepository.MarkCalendarAsSharedAsync(firstCalendarId, ct);
            changeCount += await calendarRepository.SaveChangesAsync(ct);
            // FHQ-166: a calendar's display name is its Google `summary`, which for a PRIMARY
            // calendar is the account's email address — and for a member calendar is a child's
            // name. Neither belongs in Seq; the calendar's own id says the same thing.
            logger.LogInformation(
                "Auto-designated calendar {CalendarInfoId} as the shared calendar (no prior designation).",
                firstCalendarId);
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
            logger.LogWarning("Calendar {CalendarInfoId} not found. Skipping sync.", calendarInfoId);
            return 0;
        }

        bool isNewSyncState = false;
        var syncState = await calendarRepository.GetSyncStateAsync(calendarInfoId, ct);
        if (syncState == null)
        {
            syncState = new SyncState { CalendarInfoId = calendarInfoId };
            isNewSyncState = true;
        }

        // FHQ-189: a calendar that has never been synced WITH reminders in the field mask is forced
        // through one full sync, even though its token is still valid. Incremental sync never
        // re-sends an unchanged event, so the events already in production would otherwise never
        // gain their reminders. Stamped below once a COMPLETE fetch succeeds, so for virtually every
        // calendar this happens exactly once — the exception being one that never manages a complete
        // fetch, which repeats it each run and is correct to. This is the same code path a Google 410
        // already exercises.
        bool needsReminderBackfill = syncState.RemindersSyncedAt is null;
        // Either unset forces a full sync, and an incomplete fetch clears the token below, so this is
        // also how a fetch that came back short gets re-read in full rather than deltas-only from a
        // baseline with a hole in it.
        bool isFullSync = string.IsNullOrEmpty(syncState.SyncToken) || needsReminderBackfill;

        if (needsReminderBackfill)
        {
            logger.LogInformation(
                "Calendar {CalendarInfoId} has no reminder data; forcing one full sync to backfill it.",
                calendar.Id);
        }

        logger.LogInformation("Syncing calendar {CalendarInfoId}. FullSync={IsFullSync}", calendar.Id, isFullSync);

        int changeCount = 0;

        try
        {
            // The tombstone diff's candidate rows, read BEFORE the fetch rather than after it — the
            // same ordering, for the same reason, as CalendarEventService.ReconcileWindowAsync. Only a
            // row that was already stored when the fetch was taken can be judged by that fetch: a row
            // stored afterwards — a kiosk write, or an event created on a phone that another sync of
            // this calendar has just ingested — is missing from it for that reason alone, and deleting
            // such a row would lose a real event that no incremental sync brings back, since an
            // unchanged event is never re-sent. Reading early costs nothing (the diff needs the query
            // either way) and the rows this read misses are only ever orphans the next full sync
            // removes. Conditional because an incremental sync runs no diff: the common path must not
            // pay for a query it has no use for.
            IReadOnlyList<CalendarEvent> storedBeforeFetch = isFullSync
                ? await calendarRepository.GetEventsByOwnerCalendarAsync(calendarInfoId, startDate, endDate, ct)
                : [];

            var fetch = await googleCalendarClient.GetEventsAsync(
                calendar.GoogleCalendarId,
                isFullSync ? startDate : null,
                isFullSync ? endDate : null,
                // A full sync (backfill included) is a windowed fetch, not an incremental one — Google's
                // events.list rejects a syncToken combined with a time range, so none is sent here.
                isFullSync ? null : syncState.SyncToken,
                ct);

            // Materialise once: the sequence is enumerated several times below (pass-2 resolution,
            // tombstone diff, the persistence loop, the final count) and the loop mutates each
            // instance's RecurrenceRule — a lazy sequence would re-execute and lose those writes.
            var events = fetch.Events as IReadOnlyList<CalendarEvent> ?? fetch.Events.ToList();

            var allLocalCalendars = await calendarRepository.GetCalendarsAsync(ct);
            // FHQ-46: two candidate sets for member resolution (see IMemberTagParser.ParseMembers).
            //  - memberCalendarNames (non-shared) governs the FREE-FORM fallback, so a user's free-text
            //    description that merely mentions the shared (container) calendar's name is NOT treated as
            //    a membership. Free-form member entry (any format/separator/case) is otherwise preserved.
            //  - allCalendarNames resolves an EXPLICIT "[members: ...]" tag, which is authoritative — so a
            //    tagged member is not dropped while its calendar is transiently shared (the first-login
            //    auto-designation window). The app never writes the shared calendar into a tag
            //    (NormaliseDescription strips it), so the broader set only ever rescues a legitimately-
            //    tagged member; it never invents one. The memberless-event fallback below still excludes
            //    the shared container calendar.
            var memberCalendarNames = allLocalCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
            var allCalendarNames    = allLocalCalendars.Select(c => c.DisplayName).ToList();

            // FHQ-75: Google does not enforce unique calendar names, so two calendars can share a
            // display name (case-insensitively). ToDictionary would throw and abort the whole sync;
            // build defensively instead — first wins, which is deterministic because
            // GetCalendarsAsync orders by DisplayOrder then Id. Display names are user data (family
            // member names), so the warning carries calendar ids only, never the name itself.
            var calendarByName = new Dictionary<string, CalendarInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var localCal in allLocalCalendars)
            {
                if (!calendarByName.TryAdd(localCal.DisplayName, localCal))
                {
                    logger.LogWarning(
                        "Duplicate calendar display name during sync: calendar {IgnoredCalendarId} shares a display name with calendar {KeptCalendarId}; member-name resolution uses the first-ordered calendar.",
                        localCal.Id,
                        calendarByName[localCal.DisplayName].Id);
                }
            }

            // Pass 2 (recurrence): resolve an RRULE for every recurring series referenced by the
            // pass-1 instances and cache it for this sync run, so each unknown master is fetched
            // at most once. Cancelled tombstones and self-echoes are excluded (see the resolver).
            var rruleCache = await ResolveSeriesRecurrenceRulesAsync(calendar, events, ct);

            if (isFullSync)
            {
                // Absence is only evidence when the fetch that produced it read everything Google
                // offered. GetEventsAsync reports that itself: false means it holds fewer events
                // than Google has and cannot say which ones are missing, so a row it failed to name
                // may be a live event on the family's calendar — and deleting one loses it until a
                // later full sync, since an incremental sync never re-sends an unchanged event. The
                // causes are enumerated on GoogleEventFetch.IsComplete and deliberately not
                // re-listed here; one of them is a single unusable ITEM on a page that read fine,
                // which is why a non-empty, unsuspicious-looking answer can still be short.
                //
                // An EMPTY answer is deliberately not refused here, unlike in
                // CalendarEventService.PruneRowsAbsentFromWindowFetchAsync. That prune reads back a
                // window its own operation has just written to, so an empty answer there may be
                // Google not yet reflecting that write. This sync makes no Google write of its own,
                // so it is not reading anything back and empty-and-complete is a statement about the
                // window rather than about us. Refusing it would leave no code path that ever
                // removes the orphans of an emptied window — this diff is the one that clears them.
                if (!fetch.IsComplete)
                {
                    logger.LogWarning(
                        "Full sync of calendar {CalendarInfoId} fetched {EventCount} events but reported the " +
                        "fetch incomplete, so it is no statement of what Google holds and no local row was " +
                        "tombstoned.",
                        calendar.Id, events.Count);
                }
                else
                {
                    // Tombstone events no longer present in Google. The candidates are the rows read
                    // before the fetch above; do NOT re-read them here (see the comment on that read).
                    var fetchedGoogleIds = events.Select(e => e.GoogleEventId).ToHashSet();
                    var obsoleteList     = storedBeforeFetch.Where(e => !fetchedGoogleIds.Contains(e.GoogleEventId)).ToList();

                    foreach (var obsoleteEvt in obsoleteList)
                        await calendarRepository.DeleteEventAsync(obsoleteEvt.Id, ct);

                    if (obsoleteList.Count > 0)
                        changeCount += await calendarRepository.SaveChangesAsync(ct);
                }
            }

            foreach (var evt in events)
            {
                // The entity actually written to the change tracker for this event.
                // If a downstream SaveChangesAsync throws (e.g. Postgres rejects the
                // value as too long), we need to detach this specific entity so the
                // failure does not poison subsequent per-event saves.
                CalendarEvent? touched = null;
                try
                {
                    // The self-echo guard below resolves a hash match against the locally-stored row
                    // (see IsSelfEcho), so the row is fetched for every hash candidate. The lookup
                    // is one indexed single-row read, and only an event whose id AND stamp match a
                    // write FamilyHQ itself made in the last 60 seconds pays it — never a whole sync
                    // page. An event that is not a candidate still costs nothing here: it reaches the
                    // update/create branch below, which fetches the row once anyway.
                    var isHashCandidate = !string.IsNullOrEmpty(evt.ContentHash)
                        && outboundWriteHashCache.WasRecentlyWritten(evt.GoogleEventId, evt.ContentHash);
                    var localEvent = isHashCandidate
                        ? await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct)
                        : null;

                    // Self-echo guard (FHQ-30): skip events that echo our own outbound writes.
                    // ContentHash is populated by GoogleCalendarClient from extendedProperties.private["content-hash"].
                    // Null hash means a manually-edited event, a delete tombstone, or a legacy event — always process.
                    if (IsSelfEcho(evt, localEvent))
                    {
                        logger.LogInformation(
                            "Self-echo skipped for event {EventId} on calendar {CalendarInfoId} (hash {Hash}).",
                            evt.GoogleEventId, calendarInfoId, evt.ContentHash);
                        continue;
                    }

                    // Not an echo (or not a candidate at all): the update/create branch below
                    // needs the locally-stored row regardless, so fetch it now unless the echo
                    // check above already did.
                    localEvent ??= await calendarRepository.GetEventByGoogleEventIdAsync(evt.GoogleEventId, ct);

                    // Stamp the resolved RRULE (pass 2) onto recurring instances before persistence.
                    // A series whose master could not be fetched this run is left with RecurrenceRule
                    // null so the next sync retries it.
                    if (evt.GoogleRecurringEventId is not null
                        && rruleCache.TryGetValue(evt.GoogleRecurringEventId, out var resolvedRrule))
                    {
                        evt.RecurrenceRule = resolvedRrule;
                    }

                    if (evt.Title == "CANCELLED_TOMBSTONE")
                    {
                        if (localEvent != null)
                        {
                            touched = localEvent;
                            await calendarRepository.DeleteEventAsync(localEvent.Id, ct);
                            changeCount += await calendarRepository.SaveChangesAsync(ct);
                        }
                        continue;
                    }

                    // Derive members from description: free-form fallback over member calendars only,
                    // explicit [members:] tag resolved authoritatively over all calendars (FHQ-46).
                    var parsedNames   = memberTagParser.ParseMembers(evt.Description, memberCalendarNames, allCalendarNames);
                    // Distinct: duplicate display names (FHQ-75) make the parser return the same
                    // name once per duplicate, and every occurrence resolves to the same winning
                    // calendar — collapse them so an event never gets duplicate junction rows.
                    var parsedMembers = parsedNames
                        .Where(n => calendarByName.ContainsKey(n))
                        .Select(n => calendarByName[n])
                        .Distinct()
                        .ToList();

                    // FHQ-68: the owning (source) calendar is always an attendee of its own events when it is a
                    // personal (non-shared) calendar, in addition to any members named in the description. The
                    // designated shared calendar is the container, never an attendee. Dedup against parsed members.
                    if (!calendar.IsShared && parsedMembers.All(m => m.Id != calendar.Id))
                        parsedMembers.Add(calendar);

                    if (localEvent != null)
                    {
                        touched = localEvent;
                        localEvent.Title                  = evt.Title;
                        localEvent.Start                  = evt.Start;
                        localEvent.End                    = evt.End;
                        localEvent.IsAllDay               = evt.IsAllDay;
                        localEvent.Location               = evt.Location;
                        localEvent.Description            = evt.Description;
                        localEvent.Members                = parsedMembers;
                        localEvent.GoogleRecurringEventId = evt.GoogleRecurringEventId;
                        localEvent.OriginalStartTime      = evt.OriginalStartTime;
                        // Preserve an already-stored RRULE if pass 2 could not resolve one this run
                        // (transient master-fetch failure must not blank out a known rule).
                        localEvent.RecurrenceRule         = evt.RecurrenceRule ?? localEvent.RecurrenceRule;
                        // FHQ-164 Decision 4: lazy backfill of the series' anchor zone. Google reports
                        // start.timeZone on every timed instance in the list response, so an ordinary
                        // window sync populates the column for free — no bulk job, no schema default,
                        // and no extra API call. A BLANK value counts as absent, not as a new value:
                        // an all-day event legitimately supplies none, and writing "" back would be
                        // read as "no zone" by the outbound write, which then re-anchors the series to
                        // the family's zone — FHQ-170 all over again, from a null-check that looked
                        // complete.
                        localEvent.IanaTimeZone           = string.IsNullOrWhiteSpace(evt.IanaTimeZone)
                            ? localEvent.IanaTimeZone
                            : evt.IanaTimeZone;
                        // FHQ-189: unlike IanaTimeZone above — which is only ever filled in, never
                        // cleared — reminders are authoritative on every fetch: a user really can
                        // remove the last one, and that removal must reach us. A NULL from the
                        // client means Google said nothing about reminders at all, which is not the
                        // same as "none", so the stored value stands.
                        localEvent.Reminders              = evt.Reminders ?? localEvent.Reminders;
                        await calendarRepository.UpdateEventAsync(localEvent, ct);
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

            // Persisting a sync token is the strongest statement this service makes: it tells the
            // next sync "the local rows match Google up to here, so send only what changed since".
            // An incomplete fetch has not earned it. Taking the token anyway makes the next sync
            // incremental from a baseline known to have a hole in it, and Google never re-sends an
            // unchanged event — so whatever this fetch missed stays missing until a 410 happens to
            // force a full sync. The prunes refusing to DELETE on an incomplete fetch is only half
            // the job; discarding the token is the half that gets the missing events back.
            //
            // Null is not a new recovery mechanism: it is the state the SyncTokenExpiredException
            // handler below already leaves behind, and isFullSync at the top reads it the same way.
            //
            // The shape that actually arrives here with a token worth discarding is an item-level
            // skip, where the listing ran to its final page and that page carried a token. The other
            // two causes come with a null token anyway — an unreadable page stops the loop, and the
            // page cap only trips while a nextPageToken is outstanding, which is precisely when
            // Google omits nextSyncToken. Testing IsComplete rather than the cause means this does
            // not depend on that exclusivity holding.
            syncState.SyncToken    = fetch.IsComplete ? fetch.NextSyncToken : null;
            // Unconditional: a sync did run, and this field's only reader is the diagnostics
            // connection-status DTO, which is asking when rather than how completely.
            syncState.LastSyncedAt = DateTimeOffset.UtcNow;
            if (fetch.IsComplete)
            {
                // Stamp only after a COMPLETE fetch, so a backfill that failed or came back partial
                // is retried next sync rather than being silently skipped forever. A partial fetch
                // read no reminders for the events it did not return, so stamping here would claim
                // work that was not done — and would leave this field saying "backfilled" while the
                // token above says "not in sync", which is a contradiction a later reader has to
                // resolve.
                //
                // It costs no extra full syncs: the token is nulled on exactly this condition and
                // isFullSync is true when EITHER is unset, so the full sync is already forced. The
                // one visible cost is that a calendar which never manages a complete fetch keeps
                // logging the "no reminder data; forcing one full sync" line every run — which is a
                // true statement about that calendar.
                syncState.RemindersSyncedAt ??= DateTimeOffset.UtcNow;
            }
            if (isFullSync)
            {
                syncState.SyncWindowStart = startDate;
                syncState.SyncWindowEnd   = endDate;
            }

            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);

            // Bookkeeping only — excluded from the material change count (FHQ-44).
            await calendarRepository.SaveChangesAsync(ct);
            logger.LogInformation("Synced {Count} events for calendar {CalendarInfoId}.", events.Count(), calendar.Id);
            return changeCount;
        }
        catch (SyncTokenExpiredException) when (!isRetry)
        {
            logger.LogWarning("Sync token expired for calendar {CalendarInfoId}. Restarting full sync.", calendar.Id);
            syncState.SyncToken = null;
            if (isNewSyncState) await calendarRepository.AddSyncStateAsync(syncState, ct);
            else                await calendarRepository.SaveSyncStateAsync(syncState, ct);
            // Bookkeeping only — excluded from the material change count (FHQ-44).
            await calendarRepository.SaveChangesAsync(ct);
            return await SyncCoreAsync(calendarInfoId, startDate, endDate, isRetry: true, ct);
        }
    }

    /// <summary>
    /// FHQ-164 Decision 4 / FHQ-189 / FHQ-211 applied to the CALENDAR row: adopt the name, colour,
    /// default zone and default reminders Google reports for a calendar FamilyHQ already knows about.
    /// </summary>
    /// <remarks>
    /// Nothing else refreshes an existing calendar's fields from Google — every
    /// <c>UpdateCalendarAsync</c> call site persists a flag the user changed locally — so without
    /// this, every calendar already in production would keep a null zone (and a null
    /// <c>DefaultReminders</c>) forever, and would keep the name and colour it was first inserted
    /// with forever: <see cref="AddCalendarAsync"/>-equivalent backfill only ever runs for a BRAND
    /// NEW calendar, and in production every calendar already exists. The values arrive on the
    /// <c>calendarList</c> response <see cref="SyncAllAsync"/> already fetches, so this costs no
    /// extra API call, and no separate backfill machinery is needed — the calendar list is refetched
    /// on every sync, so a stale row self-heals on the next one.
    /// <para>
    /// FHQ-211: the name and colour are NOT cosmetic. Google is the system of record and a rename
    /// made in the Google Calendar app was being dropped on the floor, while the stale name stayed
    /// load-bearing: <see cref="IMemberTagParser"/> resolves members by matching calendar DISPLAY
    /// NAMES in an event's description, so after a rename a description naming the new name stopped
    /// resolving and one naming the old name still did — event-to-member assignment broke silently in
    /// both directions. There is no local rename to protect: <c>CalendarSettingsRequest</c> carries
    /// only <c>IsVisible</c>/<c>IsShared</c>, so FamilyHQ has never let anyone rename or recolour a
    /// calendar and Google's value is unambiguously authoritative. Compared against
    /// <see cref="CalendarInfo.DisplayName"/>, which is where <c>GoogleCalendarClient</c> has already
    /// resolved <c>summaryOverride ?? summary</c> — the same field the add path stores.
    /// </para>
    /// <para>
    /// Idempotent: each field is written only when Google reports a value that differs from the
    /// stored one. A blank/absent name, colour or zone, or a null <c>DefaultReminders</c>, never
    /// blanks a stored value — all are optional on Google's calendar resource, and dropping a known
    /// value would cost the zone ladder a rung, make a change on someone's phone unreachable, or
    /// leave a calendar nameless (which would take member resolution down with it).
    /// </para>
    /// <para>
    /// The change count (FHQ-44) splits along what the dashboard RENDERS, which is why the returned
    /// count is not simply zero as it was before FHQ-211. A calendar's default zone and default
    /// reminders are invisible on screen, so adopting them stays bookkeeping. A calendar's name and
    /// colour are rendered — every Agenda column header and event chip — and
    /// <c>CalendarSyncWorker</c> broadcasts <c>EventsUpdated</c> (whose kiosk handler refetches the
    /// calendar list) only when <see cref="SyncResult.HadChanges"/>. Counting a rename as
    /// bookkeeping would therefore fix the database and leave the kiosk showing a name the family no
    /// longer uses until some unrelated event changed. Exactly one row is written per refresh, so
    /// the count reported here is that row's.
    /// </para>
    /// </remarks>
    /// <returns>The number of MATERIAL rows written — 1 when a rendered field changed, else 0.</returns>
    private async Task<int> RefreshCalendarDefaultsAsync(CalendarInfo localCal, CalendarInfo googleCal, CancellationToken ct)
    {
        var nameChanged = !string.IsNullOrWhiteSpace(googleCal.DisplayName)
            && googleCal.DisplayName != localCal.DisplayName;
        var colourChanged = !string.IsNullOrWhiteSpace(googleCal.Color)
            && googleCal.Color != localCal.Color;
        var zoneChanged = !string.IsNullOrWhiteSpace(googleCal.IanaTimeZone)
            && googleCal.IanaTimeZone != localCal.IanaTimeZone;
        var remindersChanged = googleCal.DefaultReminders is not null
            && !googleCal.DefaultReminders.SameAs(localCal.DefaultReminders);

        if (!nameChanged && !colourChanged && !zoneChanged && !remindersChanged)
            return 0;

        if (nameChanged)
        {
            // FHQ-166: neither the old nor the new name may reach Seq. A calendar's display name is
            // its Google `summary` — a family member's name, or the account's email address for a
            // primary calendar. The calendar's own id correlates just as well.
            logger.LogDebug(
                "Calendar {CalendarInfoId} adopting Google's calendar name (renamed in Google).",
                localCal.Id);
            localCal.DisplayName = googleCal.DisplayName;
        }

        if (colourChanged)
        {
            logger.LogDebug(
                "Calendar {CalendarInfoId} adopting Google's calendar colour {Color}.",
                localCal.Id, googleCal.Color);
            localCal.Color = googleCal.Color;
        }

        if (zoneChanged)
        {
            logger.LogDebug(
                "Calendar {CalendarInfoId} adopting Google's default time zone {IanaTimeZone}.",
                localCal.Id, googleCal.IanaTimeZone);
            localCal.IanaTimeZone = googleCal.IanaTimeZone;
        }

        if (remindersChanged)
        {
            logger.LogDebug(
                "Calendar {CalendarInfoId} adopting Google's default reminders.",
                localCal.Id);
            localCal.DefaultReminders = googleCal.DefaultReminders;
        }

        await calendarRepository.UpdateCalendarAsync(localCal, ct);
        var saved = await calendarRepository.SaveChangesAsync(ct);

        return nameChanged || colourChanged ? saved : 0;
    }

    /// <summary>
    /// FHQ-30 self-echo guard, narrowed by FHQ-189: true when this inbound event echoes one of our
    /// own recent writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stamp proves authorship, not freshness.</b> A matching (id, content-hash) pair says
    /// only that FamilyHQ wrote this event in the last 60 seconds. It does NOT say the content is
    /// still the content we wrote, because the hash lives in <c>extendedProperties.private</c> and
    /// Google leaves that alone when the event changes underneath it. Two ways that happens, both
    /// real: patching a series master rewrites the <c>summary</c> of every exception of that series
    /// while their extended properties keep the stamp the single-occurrence write left there; and an
    /// edit made in the Google Calendar app on a phone changes whatever the user changed and touches
    /// no extended property at all. In both cases the hash test alone discards a genuine inbound
    /// change silently and PERMANENTLY — incremental sync never re-sends an unchanged event, so the
    /// row keeps the stale value until a full sync happens to rebuild it.
    /// </para>
    /// <para>
    /// So the stamp selects a candidate and the stored row decides: an echo carries what we wrote,
    /// and what we wrote is what we stored. Every write path that records a hash persists the same
    /// content in the same operation — the kiosk create/update paths store the request they sent, and
    /// the recurring reconcile and the series migration store the very event they read the echoed
    /// hash off — so a true echo compares equal and is still skipped. This is deliberately NOT a
    /// recompute of the hash from the inbound event: <see cref="EventContentHash.Compute"/> describes
    /// what a write SENDS rather than what an event HOLDS (see its remarks), and comparing a
    /// recomputed digest with the stamp would stop recognising FamilyHQ's own writes.
    /// </para>
    /// <para>
    /// <b>Reminders</b> are compared separately because they are not in the hash at all, so a
    /// reminder added on a phone inside the window arrives carrying the hash the kiosk last stamped.
    /// I2 (FHQ-189): <c>existing.Reminders</c> can still be <c>null</c> — NOT, as an earlier version
    /// of this comment claimed, because a kiosk create starts that way. It does not: <see
    /// cref="CalendarEventService.CreateAsync"/> persists the very object
    /// <see cref="GoogleCalendarClient.CreateEventAsync"/> returns, and that call already applies
    /// Google's create response's reminders before returning — for an untouched event the Simulator
    /// (and real Google) reports <c>useDefault:true</c> immediately, so a fresh kiosk create never
    /// leaves this row null. The case this guard still exists for is a row reminders tracking has
    /// never populated at all — chiefly a pre-existing row from before this column was added, the
    /// same kind of backfill gap AGENTS.md calls out elsewhere in this codebase. Treating
    /// <c>existing.Reminders is null</c> as "nothing to compare, assume echo" would suppress the
    /// first inbound reminders report for such a row forever. When the inbound event reports
    /// reminders and we hold none yet, there is something to learn, so this is NOT an echo — the
    /// update is idempotent for every hashed field regardless.
    /// </para>
    /// </remarks>
    private bool IsSelfEcho(CalendarEvent evt, CalendarEvent? existing)
    {
        if (string.IsNullOrEmpty(evt.ContentHash)) return false;
        if (!outboundWriteHashCache.WasRecentlyWritten(evt.GoogleEventId, evt.ContentHash)) return false;

        // No locally-stored row: Google's echo of a create can arrive before our own insert has
        // committed. There is nothing to compare against and nothing to lose — the create path is
        // holding the content it just wrote — so fall back to the hash decision alone.
        if (existing is null) return true;

        // Google changed something the stamp still claims is ours: a real inbound change, not an echo.
        if (!CarriesStoredContent(evt, existing)) return false;

        // Google said nothing about reminders on this fetch, so there is nothing to compare.
        if (evt.Reminders is null) return true;

        // I2: existing has never learned any reminders but Google now reports some — not an echo.
        if (existing.Reminders is null) return false;

        return evt.Reminders.SameAs(existing.Reminders);
    }

    /// <summary>
    /// Whether an inbound event still carries the content the stored row holds, over the fields a
    /// FamilyHQ write sends.
    /// </summary>
    /// <remarks>
    /// The set is <see cref="EventContentHash.Compute"/>'s plus Location. Location is not hashed —
    /// so the stamp says nothing about it — but the stored row holds it, and a location changed
    /// elsewhere inside the cache's window is exactly the kind of real change the stamp would
    /// otherwise suppress. Reminders are compared by the caller, which has the null-vs-unlearned
    /// cases to weigh.
    /// <para>
    /// <see cref="DateTimeOffset"/> equality compares instants, not offsets, which is what is wanted
    /// here: Google may echo a start in a different offset from the one it was sent in without that
    /// being a change. A null and an empty string are the same absence for both text fields —
    /// matching how the hash renders a description, and how the outbound mapping sends a missing
    /// location as <c>""</c> where the row keeps null.
    /// </para>
    /// </remarks>
    private static bool CarriesStoredContent(CalendarEvent inbound, CalendarEvent stored) =>
        string.Equals(inbound.Title, stored.Title, StringComparison.Ordinal)
        && inbound.Start == stored.Start
        && inbound.End == stored.End
        && inbound.IsAllDay == stored.IsAllDay
        && SameText(inbound.Description, stored.Description)
        && SameText(inbound.Location, stored.Location);

    private static bool SameText(string? inbound, string? stored) =>
        string.Equals(
            string.IsNullOrEmpty(inbound) ? string.Empty : inbound,
            string.IsNullOrEmpty(stored) ? string.Empty : stored,
            StringComparison.Ordinal);

    /// <summary>
    /// Pass 2 of recurring ingestion. Builds a per-run series-id → RRULE cache from the
    /// pass-1 instances: series whose RRULE is already stored locally skip the API entirely,
    /// and each remaining unknown master is fetched exactly once. A transient master-fetch
    /// failure (null or non-reauth exception) leaves that series out of the cache so its
    /// instances persist with a null RRULE and the next sync retries; a reauth failure
    /// propagates so the user is prompted to reconnect.
    /// </summary>
    /// <remarks>
    /// Takes the whole <see cref="CalendarInfo"/> rather than just its Google id so the degraded
    /// paths below can name the calendar by FamilyHQ's own id: the Google id is an email address
    /// for a primary calendar and must not reach Seq (FHQ-166).
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string>> ResolveSeriesRecurrenceRulesAsync(
        CalendarInfo calendar, IEnumerable<CalendarEvent> events, CancellationToken ct)
    {
        // Cancelled tombstones reuse the recurring id but are being deleted, so they need no RRULE.
        // Hash candidates are excluded too, and the null passed for the stored row makes that the
        // hash-only decision on purpose: this pass runs before the per-event lookup, and its only
        // job is to avoid spending a Google master fetch. Treating every one of our own recent
        // writes as an echo here is the cheap, quota-preserving answer — the alternative puts an API
        // call behind each instance we ourselves just wrote, which is what this pass exists to
        // avoid. The persistence loop makes the real decision against the stored row, and when it
        // finds a candidate is NOT an echo the missing rule costs nothing durable: the instance
        // keeps whatever rule its row already holds, and a row that has none picks one up on the
        // next sync.
        var seriesIds = events
            .Where(e => e.GoogleRecurringEventId is not null
                     && e.Title != "CANCELLED_TOMBSTONE"
                     && !IsSelfEcho(e, null))
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
                var master = await googleCalendarClient.GetSeriesMasterAsync(calendar.GoogleCalendarId, seriesId, ct);
                // FHQ-172: a master is now returned for its DTSTART even when it carries no RRULE
                // line, so "no master" and "no rule" are separate conditions. This pass wants the
                // rule and nothing else, and its degraded behaviour is unchanged for both: cache
                // nothing, warn, and let the next sync retry the series.
                if (master?.Rrule is { } rrule)
                    cache[seriesId] = rrule;
                else
                    logger.LogWarning(
                        "Series master {SeriesId} on calendar {CalendarInfoId} returned no RRULE; instances persisted without one and will retry next sync.",
                        seriesId, calendar.Id);
            }
            catch (Exception ex) when (ex is not GoogleReauthRequiredException and not OperationCanceledException)
            {
                // Transient API failure: degrade gracefully, retry the series next sync.
                logger.LogWarning(
                    ex,
                    "Failed to fetch series master {SeriesId} on calendar {CalendarInfoId}; instances persisted without an RRULE and will retry next sync.",
                    seriesId, calendar.Id);
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
