using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Calendar.Recurrence;
using FamilyHQ.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ICalendarMigrationService migrationService,
    IMemberTagParser memberTagParser,
    IOutboundWriteHashCache outboundCache,
    ICurrentUserService currentUserService,
    IRecurrenceTimeZoneFactory recurrenceTimeZoneFactory,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var assignedMembers = request.MemberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new UnknownCalendarException(id))
            .ToList();

        // Determine target calendar
        var targetCalendar = assignedMembers.Count == 1
            ? assignedMembers[0]
            : await calendarRepository.GetSharedCalendarAsync(ct)
              ?? throw new InvalidOperationException("No shared calendar configured for multi-member events.");

        // Build description with member tag
        var memberNames = assignedMembers.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        var calendarEvent = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = fullDescription,
            OwnerCalendarInfoId = targetCalendar.Id,
            Members = assignedMembers,
            // FHQ-170: deliberately none. A brand-new event has no zone Google supplied for it, so
            // there is nothing to preserve and the family's configured zone is the right answer —
            // the one case where a FamilyHQ setting may fill in for a Google value. Stated rather
            // than left to the default so the decision is visible at the call site (and so
            // OutboundZoneGuardTests can tell "decided" from "forgotten").
            IanaTimeZone = null
        };

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        if (request.RecurrenceRule is { } rrule)
        {
            return await CreateRecurringSeriesAsync(calendarEvent, targetCalendar, hash, rrule, ct);
        }

        calendarEvent = await googleCalendarClient.CreateEventAsync(
            targetCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        outboundCache.Record(calendarEvent.GoogleEventId, hash);
        logger.LogDebug(
            "Recorded outbound write hash for event {EventId} (hash {Hash}).",
            calendarEvent.GoogleEventId, hash);

        await calendarRepository.AddEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        // FHQ-166: the Google calendar id is an email address for a primary calendar. The owning
        // CalendarInfo's own id identifies the same calendar, correlates with every other
        // {CalendarInfoId} in the sync path, and carries nothing personal.
        logger.LogInformation("Event {GoogleEventId} created on calendar {CalendarInfoId}.",
            calendarEvent.GoogleEventId, targetCalendar.Id);

        return calendarEvent;
    }

    // Native recurring creation (FHQ-18.5): create the series master via the recurrence array, then
    // reconcile the owner's window so the expanded instances persist with GoogleRecurringEventId +
    // RecurrenceRule set and each echoed instance hash recorded for the FHQ-30 self-echo guard.
    private async Task<CalendarEvent> CreateRecurringSeriesAsync(
        CalendarEvent master, CalendarInfo targetCalendar, string hash, string rrule, CancellationToken ct)
    {
        // Validate/canonicalise the supplied RRULE before any Google mutation (fail fast).
        var canonicalRule = RecurrenceRuleBuilder.ToRRuleString(RecurrenceRuleBuilder.ParseRRuleString(rrule));

        var created = await googleCalendarClient.CreateRecurringEventAsync(
            targetCalendar.GoogleCalendarId, master, hash, canonicalRule, ct);
        RecordOutbound(created.GoogleEventId, hash);

        var reconciled = await ReconcileWindowAsync(
            targetCalendar,
            new Dictionary<string, string> { [created.GoogleEventId] = canonicalRule },
            ct);

        logger.LogInformation("Recurring event {GoogleEventId} created on calendar {CalendarInfoId}.",
            created.GoogleEventId, targetCalendar.Id);

        // Return a persisted, reconciled recurring instance (consistent with the non-recurring path,
        // which returns the persisted row) rather than the unpersisted Google master object.
        return reconciled.FirstOrDefault(r => r.GoogleRecurringEventId == created.GoogleEventId)
            ?? reconciled.FirstOrDefault(r => r.IsRecurring)
            ?? created;
    }

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var userId = currentUserService.UserId ?? string.Empty;
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, userId, ct)
            ?? throw new EventNotFoundException(eventId);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        // Contradictory request: "clear recurrence" and "set a new rule" cannot both hold. Fail fast
        // rather than silently picking one (the two fields are mutually exclusive by contract).
        if (request.ClearRecurrence && request.RecurrenceRule is not null)
            throw new ContradictoryRecurrenceUpdateException();

        // Recurrence toggle: ON promotes a single event to a series in place; OFF collapses a series
        // back to one event. Both materialise/clean up the local rows via a window reconcile.
        if (request.ClearRecurrence && calendarEvent.IsRecurring)
        {
            return await ToggleRecurrenceOffAsync(calendarEvent, ownerCalendar, ct);
        }

        if (request.RecurrenceRule is { } rrule && !calendarEvent.IsRecurring)
        {
            return await ToggleRecurrenceOnAsync(calendarEvent, ownerCalendar, request, rrule, ct);
        }

        // Preserve existing member tag; only update user-visible description
        var memberNames = calendarEvent.Members.Select(m => m.DisplayName).ToList();
        var fullDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        calendarEvent.Title = request.Title;
        calendarEvent.Start = request.Start;
        calendarEvent.End = request.End;
        calendarEvent.IsAllDay = request.IsAllDay;
        calendarEvent.Location = request.Location;
        calendarEvent.Description = fullDescription;

        var hash = EventContentHash.Compute(
            calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
            calendarEvent.IsAllDay, calendarEvent.Description);

        await googleCalendarClient.PatchEventFieldsAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);

        outboundCache.Record(calendarEvent.GoogleEventId, hash);
        logger.LogDebug(
            "Recorded outbound write hash for event {EventId} (hash {Hash}).",
            calendarEvent.GoogleEventId, hash);

        await calendarRepository.UpdateEventAsync(calendarEvent, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} updated.", eventId);
        return calendarEvent;
    }

    // Recurrence ON: patch the recurrence array onto the existing single event's own id, so Google
    // promotes it to a series master in place, then reconcile the owner window so the newly expanded
    // instances persist with the series id + RRULE and every echoed hash is recorded (FHQ-30 guard).
    private async Task<CalendarEvent> ToggleRecurrenceOnAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, UpdateEventRequest request, string rrule, CancellationToken ct)
    {
        // Apply the requested field edits to the master before promoting it (members unchanged —
        // toggling recurrence is not a membership change). NormaliseDescription keeps the single
        // canonical [members: ...] tag, identical to every other write path.
        var memberNames = calendarEvent.Members.Select(m => m.DisplayName).ToList();
        var normalisedDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        var canonicalRule = RecurrenceRuleBuilder.ToRRuleString(RecurrenceRuleBuilder.ParseRRuleString(rrule));

        // Write the field edits to the existing single event by its own id, then add the recurrence
        // array onto that same id so Google promotes it to the series master in place.
        ApplyRequestFields(calendarEvent, request, normalisedDescription);
        var hash = ComputeHash(calendarEvent);
        await googleCalendarClient.PatchEventFieldsAsync(owner.GoogleCalendarId, calendarEvent, hash, ct);
        RecordOutbound(calendarEvent.GoogleEventId, hash);

        // After promotion Google promotes the single event to the series MASTER in place, so the
        // master id (== the series id) is the event's own former single id.
        var seriesId = calendarEvent.GoogleEventId;
        var originalRowId = calendarEvent.Id;
        await googleCalendarClient.PatchSeriesRecurrenceAsync(owner.GoogleCalendarId, seriesId, canonicalRule, ct);

        var reconciled = await ReconcileWindowAsync(
            owner,
            new Dictionary<string, string> { [seriesId] = canonicalRule },
            ct);

        // Google replaces the single event with COMPOUND-id expanded instances (each carrying
        // GoogleRecurringEventId == seriesId). The original non-recurring row's id is not among the
        // expansions, so it would be left behind as a stale duplicate — delete it (BLOCKER 2).
        if (reconciled.All(r => r.Id != originalRowId))
        {
            await calendarRepository.DeleteEventAsync(originalRowId, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }

        // Return a recurring row from the reconciled set (the now-series), not the stale single.
        var promoted = reconciled.FirstOrDefault(r => r.GoogleRecurringEventId == seriesId)
            ?? reconciled.FirstOrDefault(r => r.IsRecurring);
        if (promoted is null)
            throw new InvalidOperationException(
                $"Recurrence-on for event {calendarEvent.Id} produced no recurring instances after reconcile.");

        logger.LogInformation("Event {EventId} promoted to recurring series {SeriesId}.", calendarEvent.Id, seriesId);
        return promoted;
    }

    // Recurrence OFF: clear the recurrence array on Google (collapses the series back to one event),
    // then reconcile the owner window so the collapsed single event — which Google now returns with
    // GoogleEventId == seriesId, no recurringEventId and no RRULE — is upserted as a clean single
    // row, and finally delete the leftover expanded-instance rows the reconcile did not touch.
    //
    // The toggled row's GoogleEventId is a COMPOUND instance id, NEVER equal to the master id, so a
    // "find the survivor by GoogleEventId == seriesId" approach finds nothing in production (BLOCKER 1).
    private async Task<CalendarEvent> ToggleRecurrenceOffAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, CancellationToken ct)
    {
        var seriesId = calendarEvent.GoogleRecurringEventId!;

        // Capture the expanded-instance rows BEFORE the collapse: each has GoogleRecurringEventId ==
        // seriesId and a compound GoogleEventId != seriesId. The collapsed single (id == seriesId) is
        // not part of this set, so these are exactly the rows to prune after the reconcile.
        var instanceRows = await calendarRepository.GetEventsBySeriesIdAsync(seriesId, ct);

        await googleCalendarClient.ClearSeriesRecurrenceAsync(owner.GoogleCalendarId, seriesId, ct);

        // Reconcile the window: the collapsed single event (no RRULE) is upserted as a clean,
        // non-recurring row, and its echoed ContentHash is recorded through the normal guard path.
        var reconciled = await ReconcileWindowAsync(owner, new Dictionary<string, string>(), ct);

        // Delete every former expanded-instance row (compound id != seriesId). The reconcile only
        // touched the collapsed single (id == seriesId), so these rows are now orphaned.
        var removedAny = false;
        foreach (var row in instanceRows.Where(r => r.GoogleEventId != seriesId))
        {
            await calendarRepository.DeleteEventAsync(row.Id, ct);
            removedAny = true;
        }

        if (removedAny)
            await calendarRepository.SaveChangesAsync(ct);

        // The surviving clean single is the reconciled row whose id == the (former) series id and which
        // now carries no recurrence link or rule.
        var survivor = reconciled.FirstOrDefault(r => r.GoogleEventId == seriesId);
        if (survivor is not null && survivor.IsRecurring)
            throw new InvalidOperationException(
                $"Recurrence-off for series {seriesId} left the collapsed event still marked recurring.");

        logger.LogInformation("Recurring series {SeriesId} collapsed to a single event.", seriesId);
        return survivor ?? calendarEvent;
    }

    public async Task<CalendarEvent> SetMembersAsync(
        Guid eventId,
        IReadOnlyList<Guid> memberCalendarInfoIds,
        CancellationToken ct = default)
    {
        if (memberCalendarInfoIds.Count == 0)
            throw new NoMembersException();

        var userId = currentUserService.UserId ?? string.Empty;
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, userId, ct)
            ?? throw new EventNotFoundException(eventId);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var newMembers = memberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new UnknownCalendarException(id))
            .ToList();

        // Update description with new member tag
        var strippedDescription = memberTagParser.StripMemberTag(calendarEvent.Description);
        var memberNames = newMembers.Select(m => m.DisplayName).ToList();
        calendarEvent.Description = memberTagParser.NormaliseDescription(strippedDescription, memberNames);
        calendarEvent.Members = newMembers;

        // Migrate if the individual/shared invariant is violated.
        // EnsureCorrectCalendarAsync already writes to Google and saves the DB if it migrates.
        var migrated = await migrationService.EnsureCorrectCalendarAsync(calendarEvent, newMembers, ct);

        if (migrated)
        {
            // On the migration path, CalendarMigrationService records the outbound hash on the new event id (see FHQ-30.3).
        }
        else
        {
            // No migration: write updated description/members to Google and DB.
            var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
                ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");
            var hash = EventContentHash.Compute(
                calendarEvent.Title, calendarEvent.Start, calendarEvent.End,
                calendarEvent.IsAllDay, calendarEvent.Description);

            await googleCalendarClient.PatchEventFieldsAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);

            outboundCache.Record(calendarEvent.GoogleEventId, hash);
            logger.LogDebug(
                "Recorded outbound write hash for event {EventId} (hash {Hash}).",
                calendarEvent.GoogleEventId, hash);

            await calendarRepository.UpdateEventAsync(calendarEvent, ct);
            await calendarRepository.SaveChangesAsync(ct);
        }

        logger.LogInformation("Members updated for event {EventId}.", eventId);
        return calendarEvent;
    }

    public async Task DeleteAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUserService.UserId ?? string.Empty;
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, userId, ct)
            ?? throw new EventNotFoundException(eventId);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        await googleCalendarClient.DeleteEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent.GoogleEventId, ct);
        await calendarRepository.DeleteEventAsync(eventId, ct);
        await calendarRepository.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} deleted.", eventId);
    }

    public async Task<CalendarEvent> UpdateRecurringAsync(
        Guid eventId, UpdateEventRequest request, RecurrenceScope scope, CancellationToken ct = default)
    {
        var (calendarEvent, ownerCalendar) = await LoadRecurringEventAsync(eventId, ct);

        await RejectMemberChangeOutsideAllScopeAsync(calendarEvent, request, scope, ct);

        // Member names are preserved from the existing event — UpdateEventRequest never changes
        // membership (that is SetMembersAsync's job). NormaliseDescription guarantees exactly one
        // canonical [members: ...] tag on every recurring write (spec §10.1.1).
        var memberNames = calendarEvent.Members.Select(m => m.DisplayName).ToList();
        var normalisedDescription = memberTagParser.NormaliseDescription(request.Description, memberNames);

        // series-id → RRULE for every series this operation touches, so the reconcile can stamp the
        // rule onto the (RRULE-less, pass-1) instances it materialises instead of persisting null.
        IReadOnlyDictionary<string, string> seriesRules;

        switch (scope)
        {
            case RecurrenceScope.ThisOnly:
                await PatchInstanceAsync(calendarEvent, ownerCalendar, request, normalisedDescription, ct);
                seriesRules = SeriesRuleForExisting(calendarEvent);
                break;

            case RecurrenceScope.ThisAndFollowing:
                seriesRules = await SplitSeriesAsync(calendarEvent, ownerCalendar, request, normalisedDescription, ct);
                break;

            case RecurrenceScope.AllInSeries:
                // A member change at AllInSeries that crosses the 1↔N boundary moves the whole series
                // to the correct calendar (spec §10.1.3); the migration does its own reconcile + hashing.
                if (await TryMigrateSeriesForMemberChangeAsync(calendarEvent, request, ct))
                {
                    logger.LogInformation("Recurring event {EventId} updated at scope {Scope} via series migration.", eventId, scope);
                    return calendarEvent;
                }

                await PatchSeriesMasterAsync(calendarEvent, ownerCalendar, request, normalisedDescription, ct);
                // The master patch leaves the RRULE untouched, so the series keeps its stored rule and
                // its instances keep their recurringEventId — which is what the reconcile stamps on.
                seriesRules = SeriesRuleForExisting(calendarEvent);
                break;

            default:
                throw new UnknownRecurrenceScopeException(scope);
        }

        await ReconcileWindowAsync(ownerCalendar, seriesRules, ct);

        logger.LogInformation("Recurring event {EventId} updated at scope {Scope}.", eventId, scope);
        return calendarEvent;
    }

    public async Task DeleteRecurringAsync(Guid eventId, RecurrenceScope scope, CancellationToken ct = default)
    {
        var (calendarEvent, ownerCalendar) = await LoadRecurringEventAsync(eventId, ct);
        var seriesId = calendarEvent.GoogleRecurringEventId!;

        switch (scope)
        {
            case RecurrenceScope.ThisOnly:
                await googleCalendarClient.DeleteEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent.GoogleEventId, ct);
                await calendarRepository.DeleteEventAsync(eventId, ct);
                await calendarRepository.SaveChangesAsync(ct);
                break;

            case RecurrenceScope.ThisAndFollowing:
                // Truncating the original master to UNTIL = split - 1s collapses the tail; this works
                // for COUNT-bounded series too (no occurrence counting needed for a pure delete).
                var truncatedRule = TruncateRuleBefore(calendarEvent);
                await googleCalendarClient.PatchSeriesRecurrenceAsync(ownerCalendar.GoogleCalendarId, seriesId, truncatedRule, ct);
                await RemoveSeriesRowsFromSplitAsync(seriesId, calendarEvent.Start, ct);
                break;

            case RecurrenceScope.AllInSeries:
                await googleCalendarClient.DeleteEventAsync(ownerCalendar.GoogleCalendarId, seriesId, ct);
                await RemoveSeriesRowsFromSplitAsync(seriesId, splitFrom: null, ct);
                break;

            default:
                throw new UnknownRecurrenceScopeException(scope);
        }

        logger.LogInformation("Recurring event {EventId} deleted at scope {Scope}.", eventId, scope);
    }

    // ── Recurring edit/delete helpers ─────────────────────────────────────────

    private async Task<(CalendarEvent Event, CalendarInfo Owner)> LoadRecurringEventAsync(Guid eventId, CancellationToken ct)
    {
        var userId = currentUserService.UserId ?? string.Empty;
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, userId, ct)
            ?? throw new EventNotFoundException(eventId);

        if (!calendarEvent.IsRecurring)
            throw new NotPartOfRecurringSeriesException(eventId);

        var owner = await calendarRepository.GetCalendarByIdAsync(calendarEvent.OwnerCalendarInfoId, ct)
            ?? throw new InvalidOperationException(
                $"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        return (calendarEvent, owner);
    }

    // Member-set changes are only permitted at AllInSeries (spec §10.1.2). The request can only
    // carry a change via an explicit [members: ...] tag embedded in its description differing from
    // the event's current members — reject it at the per-instance/following scopes. Detection keys
    // ONLY on an explicit tag (ExtractTaggedMembers, no whole-word fallback) so plain description
    // text that merely mentions a member's name is never mistaken for a membership change. Parsing
    // is done against ALL known member calendars (not just the event's current members) so that
    // adding a brand-new member is recognised as a change rather than silently dropped.
    private async Task RejectMemberChangeOutsideAllScopeAsync(
        CalendarEvent calendarEvent, UpdateEventRequest request, RecurrenceScope scope, CancellationToken ct)
    {
        if (scope == RecurrenceScope.AllInSeries)
            return;

        var allKnownNames = (await calendarRepository.GetCalendarsAsync(ct))
            .Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
        var requestedNames = memberTagParser.ExtractTaggedMembers(request.Description, allKnownNames);
        if (requestedNames.Count == 0)
            return; // no explicit [members:...] tag in the request → no member change

        var current = calendarEvent.Members.Select(m => m.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!requestedNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current))
            throw new MemberScopeViolationException();
    }

    private async Task PatchInstanceAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, UpdateEventRequest request, string normalisedDescription, CancellationToken ct)
    {
        // events.patch on the instance's OWN GoogleEventId — Google turns it into an exception.
        ApplyRequestFields(calendarEvent, request, normalisedDescription);

        var hash = ComputeHash(calendarEvent);
        await googleCalendarClient.PatchEventFieldsAsync(owner.GoogleCalendarId, calendarEvent, hash, ct);
        RecordOutbound(calendarEvent.GoogleEventId, hash);
    }

    // At AllInSeries, the request's description may carry a [members: ...] tag changing the member
    // set. When that change crosses the single/shared (1↔N) boundary the series must move calendars
    // — delegate to the series migration. Returns true if a migration was performed.
    private async Task<bool> TryMigrateSeriesForMemberChangeAsync(
        CalendarEvent calendarEvent, UpdateEventRequest request, CancellationToken ct)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var knownNames = allCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
        var requestedNames = memberTagParser.ExtractTaggedMembers(request.Description, knownNames);
        if (requestedNames.Count == 0)
            return false; // no explicit [members:...] tag → no member change

        var requestedMembers = allCalendars
            .Where(c => requestedNames.Contains(c.DisplayName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var currentlyShared = calendarEvent.Members.Count > 1;
        var shouldBeShared = requestedMembers.Count > 1;
        if (currentlyShared == shouldBeShared)
            return false; // member change does not cross the 1↔N boundary

        return await migrationService.EnsureCorrectCalendarForSeriesAsync(
            calendarEvent.GoogleRecurringEventId!, requestedMembers, ct);
    }

    private async Task PatchSeriesMasterAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, UpdateEventRequest request, string normalisedDescription, CancellationToken ct)
    {
        // events.patch on the series master: merge semantics, so Google preserves the master's RRULE
        // and its existing exceptions. A full-resource PUT here sends no `recurrence` array and
        // collapses the whole series into a single non-recurring event (FHQ-144).
        //
        // The edit arrives on ONE occurrence (calendarEvent), which may not be the series origin. The
        // master's DTSTART anchors the whole series, so writing the edited occurrence's absolute start
        // onto the master would relocate the series to that occurrence's date. Instead shift the
        // master's DTSTART by the same DELTA the user applied to the edited occurrence: the outcome
        // then depends on WHAT changed, not WHICH occurrence was edited (a pure time change keeps the
        // origin date; an unchanged save moves nothing). (FHQ-144 follow-up.)
        var seriesId = calendarEvent.GoogleRecurringEventId!;
        var seriesRows = await calendarRepository.GetEventsBySeriesIdAsync(seriesId, ct);
        var masterAnchor = await ResolveSeriesAnchorAsync(owner, seriesId, seriesRows, calendarEvent.Start, ct);

        var startShift = request.Start - calendarEvent.Start;
        var newMasterStart = masterAnchor.Start + startShift;
        var newMasterEnd = newMasterStart + (request.End - request.Start);

        // FHQ-172. Everything below the anchor depends on it being the series' TRUE origin. When it
        // is the local-row proxy instead, `newMasterStart` is that proxy plus the shift, and writing
        // it moves the series' origin forward to the earliest row the sync window happens to reach —
        // silently deleting every occurrence before it, on every device. `startShift` is zero for a
        // pure title edit, so even renaming a series was enough to trigger it.
        //
        // The remedy depends on what the user actually asked for:
        //   * timing unchanged → the master's own start and end are not part of the edit, so omit
        //     them and let events.patch's merge leave Google's values exactly where they are;
        //   * timing changed → the new origin is a FUNCTION of the old one, which is unknown. There
        //     is no honest value to send, so refuse rather than relocate the series.
        //
        // HOW REACHABLE IS THIS, HONESTLY. The one trigger anyone could actually trace was
        // GetSeriesMasterAsync discarding a perfectly good DTSTART whenever the master carried no
        // RRULE line; returning the start in that case (FHQ-172 Change 1) is what fixes it, and it
        // removes the RDATE-only trigger outright. What remains is a master events.get that 404s or
        // yields no parsable start — and no production shape has been demonstrated in which the
        // omit-times write below then both fires AND succeeds, because an events.patch to the same
        // id would 404 too. So treat what follows as defence-in-depth, not as the mechanism that
        // fixes the headline defect: the failure it guards against is the irreversible destruction
        // of a family's calendar history, which is worth a branch that may never run.
        var timingChange = DescribeTimingChange(calendarEvent, request, startShift);

        if (!masterAnchor.MasterResolved && timingChange is not null)
        {
            // The ONE Warning for this incident (FHQ-161's rule). DomainExceptionHandler will log a
            // second Warning when it maps the exception below, but that line carries only the status,
            // the HTTP method and the path — not which series, which calendar, or why. This line is
            // the only record of the cause, so it is additional information rather than a duplicate.
            // The anchor site above deliberately stays at Debug so this is not a third.
            logger.LogWarning(
                "Refusing an all-in-series {TimingChange} on series {SeriesId} (calendar {CalendarInfoId}): the master supplied no start, so the series' new origin cannot be derived without relocating it. Nothing was written.",
                timingChange, seriesId, owner.Id);

            throw new SeriesOriginUnresolvedException(
                "This series' original start could not be read from Google, so its times cannot be " +
                "changed for the whole series without moving the series itself. Nothing has been " +
                "changed. If the series still exists in Google Calendar this may clear on a retry; " +
                "if it does not, edit the series in the Google Calendar app instead.");
        }

        var master = new CalendarEvent
        {
            GoogleEventId = seriesId,
            Title = request.Title,
            // Meaningful only on the resolved path. On the degraded one these hold proxy + shift and
            // are never sent: PatchEventFieldsPreservingTimesAsync omits both keys, and the hash is
            // computed without them — and without IsAllDay, which reaches Google only through those
            // same keys (see ComputeHashWithoutTimes). They are left populated rather than zeroed so
            // a future reader cannot mistake a sentinel for a real origin.
            Start = newMasterStart,
            End = newMasterEnd,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = normalisedDescription,
            // FHQ-170: the master keeps the zone GOOGLE anchored the series to. Without this the
            // write falls through to the family's configured zone and re-anchors the whole series,
            // moving every future occurrence at the next divergent DST transition. The master's own
            // zone leads the stored one here (unlike the counting ladder) because the edited row may
            // be an EXCEPTION instance, which can carry a zone of its own that is not the series'.
            // Blank counts as absent, not as a value: a `"timeZone": ""` on the master would
            // otherwise beat a perfectly good stored zone and land straight back on the family's.
            IanaTimeZone = string.IsNullOrWhiteSpace(masterAnchor.TimeZoneId)
                ? StoredSeriesZone(seriesRows)
                : masterAnchor.TimeZoneId
        };

        if (masterAnchor.MasterResolved)
        {
            var hash = ComputeHash(master);
            await googleCalendarClient.PatchEventFieldsAsync(owner.GoogleCalendarId, master, hash, ct);
            RecordOutbound(master.GoogleEventId, hash);
            return;
        }

        // The degraded-but-honest write: title, location and description land; start and end are not
        // sent at all, so Google keeps the master's own DTSTART and duration. See the reachability
        // note above — this is a guard against destroying series history, with no demonstrated
        // production trigger after Change 1, not the fix for the reported defect.
        var partialHash = ComputeHashWithoutTimes(master);
        logger.LogInformation(
            "Patching series master {SeriesId} (calendar {CalendarInfoId}) without start or end: the master's own origin could not be read, so it is left as Google holds it and only the edited fields are written.",
            seriesId, owner.Id);

        await googleCalendarClient.PatchEventFieldsPreservingTimesAsync(
            owner.GoogleCalendarId, master, partialHash, ct);
        RecordOutbound(master.GoogleEventId, partialHash);
    }

    /// <summary>
    /// A short description of the timing change an all-in-series request asks for, or null when it
    /// asks for none. Start, duration and the all-day flag are each a timing change: the all-day
    /// flag is expressed to Google purely through <c>start.date</c> vs <c>start.dateTime</c>, so
    /// flipping it without sending start and end would not reach Google at all.
    /// </summary>
    private static string? DescribeTimingChange(
        CalendarEvent calendarEvent, UpdateEventRequest request, TimeSpan startShift)
    {
        if (request.IsAllDay != calendarEvent.IsAllDay)
            return "all-day change";

        if (startShift != TimeSpan.Zero)
            return "start-time change";

        return (request.End - request.Start) != (calendarEvent.End - calendarEvent.Start)
            ? "duration change"
            : null;
    }

    // Returns the series-id → RRULE map for the two series this split touches (the truncated
    // original and the new forward series), so the reconcile stamps the right rule onto each.
    private async Task<IReadOnlyDictionary<string, string>> SplitSeriesAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, UpdateEventRequest request, string normalisedDescription, CancellationToken ct)
    {
        var seriesId = calendarEvent.GoogleRecurringEventId!;
        var originalRule = calendarEvent.RecurrenceRule
            ?? throw new InvalidOperationException($"Recurring event {calendarEvent.Id} has no stored RecurrenceRule to split.");

        // Load the original series' local rows up-front: the earliest is the enumeration anchor for
        // computing the forward series' remaining COUNT, and the same rows are pruned at the split.
        var seriesRows = await calendarRepository.GetEventsBySeriesIdAsync(seriesId, ct);

        // (a) Work out the forward series' RRULE and anchor zone FIRST. This step only READS from
        // Google (the master, and at most a handful of instances) plus a local zone backfill, so it
        // does not depend on the truncation below — and putting it first is what makes a refusal or
        // a transient failure here leave Google entirely untouched. Reversed, an unresolvable master
        // aborted the split AFTER the original series had already been truncated, leaving the family
        // with a series that had lost its tail and gained no replacement (FHQ-172). The residual
        // window — truncate succeeds, create fails — is FHQ-173.
        //
        // The reorder does mean the zone ladder's backfill (PersistSeriesZoneAsync) now COMMITS its
        // SaveChangesAsync before any Google write. That is benign and deliberate: the row it writes
        // is a zone Google itself supplied for this series, cached locally — it records what Google
        // holds, not a claim that FamilyHQ wrote anything. If the split then refuses or fails, the
        // rows are left more accurate than they were, and the next attempt reads the zone for free.
        var reshaped = await ReshapeRuleAsync(owner, seriesId, originalRule, seriesRows, calendarEvent.Start, ct);
        var freshRule = reshaped.Rrule;

        // (b) Truncate the original master: re-emit its RRULE with UNTIL = this instance's start − 1s.
        var truncatedRule = TruncateRuleBefore(calendarEvent);
        await googleCalendarClient.PatchSeriesRecurrenceAsync(owner.GoogleCalendarId, seriesId, truncatedRule, ct);

        // (c) Insert a NEW recurring series from this instance with the edited values and the fresh
        // RRULE shaped like the original (preserving its end condition — see ReshapeRule).

        var newSeries = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = normalisedDescription,
            // FHQ-170: the forward half of a split is a CONTINUATION of the original series, so it
            // is anchored to the same zone Google anchored that series to. Null here (nothing
            // resolvable) leaves the client's family-zone fallback in place — today's behaviour.
            IanaTimeZone = reshaped.IanaTimeZone
        };

        var hash = ComputeHash(newSeries);
        var created = await googleCalendarClient.CreateRecurringEventAsync(owner.GoogleCalendarId, newSeries, hash, freshRule, ct);
        RecordOutbound(created.GoogleEventId, hash);

        // Remove the truncated original's local rows at/after the split point; the reconcile that
        // follows re-fetches the window and materialises the new series' instances.
        await RemoveSeriesRowsFromSplitAsync(seriesId, calendarEvent.Start, ct);

        return new Dictionary<string, string>
        {
            [seriesId] = truncatedRule,
            [created.GoogleEventId] = freshRule
        };
    }

    // The series this instance belongs to keeps its stored RRULE (used by ThisOnly/AllInSeries,
    // neither of which changes the recurrence rule). Empty when the instance has no stored rule.
    private static IReadOnlyDictionary<string, string> SeriesRuleForExisting(CalendarEvent calendarEvent)
    {
        var rules = new Dictionary<string, string>();
        if (calendarEvent.GoogleRecurringEventId is { } seriesId && calendarEvent.RecurrenceRule is { } rule)
            rules[seriesId] = rule;
        return rules;
    }

    // Re-emit the original RRULE with UNTIL set to one second before the split instant, so the
    // truncated series ends just before the instance the user split on (spec §4 / §4 delete).
    private static string TruncateRuleBefore(CalendarEvent calendarEvent)
    {
        var rrule = calendarEvent.RecurrenceRule
            ?? throw new InvalidOperationException($"Recurring event {calendarEvent.Id} has no stored RecurrenceRule to truncate.");

        var spec = RecurrenceRuleBuilder.ParseRRuleString(rrule);
        var until = calendarEvent.Start.ToUniversalTime().AddSeconds(-1);
        return RecurrenceRuleBuilder.ToRRuleString(spec with { End = RecurrenceEnd.Until(until) });
    }

    // Re-emit an RRULE for the new forward series, preserving the original end condition.
    // A bounded UNTIL series keeps the SAME UNTIL (it must not run forever on Google); a Never
    // series stays Never. A COUNT series carries the REMAINING count: the original COUNT minus the
    // occurrences that fall strictly before the split (those stay in the truncated original).
    //
    // The remaining count must be anchored at the TRUE master DTSTART (fetched via GetSeriesMaster):
    // when the master predates the synced window the earliest LOCAL row under-counts the occurrences
    // before the split, leaving the forward series too long (Major 3). FHQ-172: if the master cannot
    // be resolved the split is REFUSED rather than counted from that proxy — the count is written
    // back to Google, so a count we know may be wrong would add occurrences to the family's calendar.
    //
    // FHQ-161: the count must also enumerate IN THE SERIES' OWN ZONE. Both inputs are true
    // wall-clock-anchored instants (the master DTSTART from Google, the instance start as synced),
    // so a fixed-UTC enumeration between them drifts once the series crosses a DST transition. Across
    // a fall-back the enumerated twin of the split occurrence lands an hour early, is wrongly counted
    // as "before", and the forward series silently loses its last occurrence.
    private async Task<ReshapedSeries> ReshapeRuleAsync(
        CalendarInfo owner, string seriesId, string rrule,
        IReadOnlyList<CalendarEvent> seriesRows, DateTimeOffset splitStart, CancellationToken ct)
    {
        var spec = RecurrenceRuleBuilder.ParseRRuleString(rrule);

        if (spec.End.Kind != RecurrenceEndKind.Count)
        {
            // Never/Until preserved as-is. There is no count to enumerate, so no zone to discover
            // either: the forward series inherits whatever zone the series' own rows already carry
            // (ladder rung 1, no call), and the two FETCHING rungs stay off a path that has no use
            // for them.
            return new ReshapedSeries(RecurrenceRuleBuilder.ToRRuleString(spec), StoredSeriesZone(seriesRows));
        }

        var anchor = await ResolveSeriesAnchorAsync(owner, seriesId, seriesRows, splitStart, ct);

        // FHQ-172. Refuse before anything else runs — before the zone ladder's fetches, and (because
        // this whole method now precedes the truncation) before any write reaches Google. The count
        // below is `original COUNT − occurrences before the split`, enumerated FROM the anchor; with
        // the local-row proxy standing in for a master that predates the sync window, that
        // under-counts and the forward series carries occurrences the family never scheduled.
        if (!anchor.MasterResolved)
        {
            // The ONE Warning for this incident (FHQ-161's rule). As at the all-in-series refusal:
            // DomainExceptionHandler logs its own Warning when it maps the exception, but that line
            // names only the status, method and path, so this one is not a duplicate of it. The
            // anchor site logs at Debug precisely so this stays the only Warning we raise.
            logger.LogWarning(
                "Refusing a \"this and following\" split of COUNT-bounded series {SeriesId} (calendar {CalendarInfoId}): the master supplied no start, so the remaining count could only be derived from the earliest local row and may be wrong. Nothing was written.",
                seriesId, owner.Id);

            throw new SeriesOriginUnresolvedException(
                "This series' original start could not be read from Google, so the number of " +
                "occurrences left after this one cannot be worked out reliably. Nothing has been " +
                "changed. If the series still exists in Google Calendar this may clear on a retry; " +
                "if it does not, split the series in the Google Calendar app instead.");
        }

        var seriesZoneId = await ResolveSeriesZoneIdAsync(owner, seriesId, seriesRows, anchor, ct);
        var seriesZone = CreateRecurrenceZone(seriesId, seriesZoneId, seriesRows);
        var before = RecurrenceRuleBuilder.CountOccurrencesBefore(spec, anchor.Start, splitStart, seriesZone);
        var remaining = spec.End.Occurrences!.Value - before;

        if (remaining < 1)
            throw new InvalidSeriesSplitException(
                $"Cannot split a COUNT-based series: the split point leaves no occurrences for the " +
                $"forward series (original COUNT {spec.End.Occurrences}, {before} occurrences before the split).");

        return new ReshapedSeries(
            RecurrenceRuleBuilder.ToRRuleString(spec with { End = RecurrenceEnd.Count(remaining) }),
            seriesZoneId);
    }

    /// <summary>
    /// The RRULE for the forward half of a split, and the IANA zone that series is anchored to.
    /// The zone travels with the rule because both describe the SAME continuation: reshaping the
    /// rule without carrying the zone would hand the new series to the family-zone fallback and
    /// re-anchor it (FHQ-170).
    /// </summary>
    /// <param name="IanaTimeZone">Null when no rung of the discovery ladder yielded a zone.</param>
    private readonly record struct ReshapedSeries(string Rrule, string? IanaTimeZone);

    /// <summary>
    /// Where a recurring write's series origin came from. <c>MasterResolved</c> distinguishes the
    /// true Google DTSTART from the local-row proxy, and it is the caller's job to act on it —
    /// FHQ-172 made the proxy unwritable, not usable.
    /// </summary>
    private readonly record struct SeriesAnchor(DateTimeOffset Start, string? TimeZoneId, bool MasterResolved);

    /// <summary>
    /// The true master DTSTART (and the IANA zone it is anchored to) when the master is resolvable;
    /// otherwise the earliest local row, or the split instant when no rows exist, flagged
    /// <c>MasterResolved: false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This serves two callers, and the flag means something different to each.</b>
    /// <see cref="ReshapeRuleAsync"/> writes a COUNT derived from the anchor, and
    /// <see cref="PatchSeriesMasterAsync"/> writes the anchor itself (shifted) as the master's new
    /// DTSTART. Neither may use the proxy: the first would add or drop occurrences, the second would
    /// relocate the series' origin and delete its history. So the COUNT split refuses outright, and
    /// the master patch refuses a timing change and otherwise omits start/end from the write.
    /// </para>
    /// <para>
    /// The proxy is still returned rather than replaced by null, because <c>Start</c> stays the
    /// enumeration anchor for every resolved case and a nullable would push the same check into
    /// arithmetic. Only <c>MasterResolved</c> is load-bearing.
    /// </para>
    /// <para>
    /// A transient master-fetch failure is NOT handled here: there is no try/catch, so it propagates
    /// out through the resilient client after its retries. Only a null return — a 404, or a master
    /// with no resolvable start — reaches the degraded branch.
    /// </para>
    /// </remarks>
    private async Task<SeriesAnchor> ResolveSeriesAnchorAsync(
        CalendarInfo owner, string seriesId, IReadOnlyList<CalendarEvent> seriesRows, DateTimeOffset splitStart, CancellationToken ct)
    {
        var localAnchor = seriesRows.Count > 0 ? seriesRows.Min(r => r.Start) : splitStart;

        var master = await googleCalendarClient.GetSeriesMasterAsync(owner.GoogleCalendarId, seriesId, ct);
        if (master is null)
        {
            // Debug, not Warning. This method reports a fact and decides nothing: one of its two
            // callers handles the missing origin COMPLETELY successfully (the omit-times rename
            // lands the user's whole edit), and the logging standard is explicit that an
            // expected-and-handled condition is not a Warning. The Warning belongs to whichever
            // caller actually degrades or refuses, so that one incident yields exactly one of them.
            logger.LogDebug(
                "Series master {SeriesId} on calendar {CalendarInfoId} returned no start, so the series' true origin is unknown; no start derived from local rows will be written back to Google.",
                seriesId, owner.Id);
            return new SeriesAnchor(localAnchor, null, MasterResolved: false);
        }

        return new SeriesAnchor(master.Start, master.TimeZone, MasterResolved: true);
    }

    /// <summary>
    /// FHQ-164 Decision 2 — the series' IANA zone, ASKED OF GOOGLE rather than guessed, strictly
    /// ordered by provenance:
    /// <list type="number">
    ///   <item><description><b>Stored</b> zone on the series' own rows — no call.</description></item>
    ///   <item><description>The <b>series master</b>, already fetched for the anchor — persisted on the way past.</description></item>
    ///   <item><description>Any <b>surviving instance</b> via events.get — persisted on the way past.</description></item>
    ///   <item><description>The <b>calendar's default</b> zone, as synced from Google's calendar resource — no call.</description></item>
    ///   <item><description>Terminal: null, and the caller degrades to fixed-UTC enumeration.</description></item>
    /// </list>
    /// Every rung but the last returns a value <b>Google supplied</b>, which is what makes this
    /// compatible rather than a better guess. The family's configured zone is deliberately absent:
    /// most events are created on a phone, so it is a proxy, and this value is written back to
    /// Google as a COUNT — substituting a local setting for a real Google value is exactly what the
    /// prime directive forbids.
    /// <para>
    /// <b>Hot paths.</b> Only rungs 2 and 3 call Google, and this method runs only on the "this and
    /// following" split of a COUNT-bounded series — a foreground, one-per-user-action path. Rung 2's
    /// fetch already happened for the anchor, so the ladder's marginal cost is rung 3 alone, and only
    /// when the two rungs above it produced nothing. Sync's per-event loop never reaches here: it
    /// gets the same value for free from <c>start.timeZone</c> on the list response.
    /// </para>
    /// <para>
    /// <b>Every rung is filtered for usability</b> (see <see cref="UsableZone"/>): a rung only
    /// short-circuits the ladder when the id it offers is one the tz database actually resolves.
    /// Google's zone names run ahead of a bundled tzdb (<c>Europe/Kyiv</c>, <c>America/Ciudad_Juarez</c>),
    /// so accepting an unrecognised id would skip the rungs below it and count in fixed-UTC when a
    /// lower rung could still have supplied a zone that works.
    /// </para>
    /// </summary>
    private async Task<string?> ResolveSeriesZoneIdAsync(
        CalendarInfo owner, string seriesId, IReadOnlyList<CalendarEvent> seriesRows,
        SeriesAnchor anchor, CancellationToken ct)
    {
        // Rung 1 — already stored locally from an earlier fetch. Free, and the reason backfill pays.
        if (UsableZone(StoredSeriesZone(seriesRows), seriesId) is { } stored)
            return stored;

        // Rung 2 — the master Google just returned for the anchor.
        if (UsableZone(anchor.TimeZoneId, seriesId) is { } masterZone)
        {
            await PersistSeriesZoneAsync(seriesRows, masterZone, ct);
            return masterZone;
        }

        // Rung 3 — any surviving instance carries the series' start.timeZone.
        var probedZone = await FetchZoneFromSurvivingInstanceAsync(owner, seriesId, seriesRows, ct);
        if (UsableZone(probedZone, seriesId) is { } instanceZone)
        {
            await PersistSeriesZoneAsync(seriesRows, instanceZone, ct);
            return instanceZone;
        }

        // Rung 4 — the calendar's own default: what Google applies to an event on it with no zone of
        // its own. NOT persisted onto the series rows — it is the calendar's value, not the series'.
        if (UsableZone(owner.IanaTimeZone, seriesId) is { } calendarZone)
        {
            logger.LogDebug(
                "Series {SeriesId} supplied no zone of its own; anchoring its COUNT split to calendar {CalendarInfoId}'s default zone {IanaTimeZone}.",
                seriesId, owner.Id, calendarZone);
            return calendarZone;
        }

        // Rung 5 — terminal. CreateRecurrenceZone degrades to fixed-UTC and reports it.
        return null;
    }

    /// <summary>
    /// A rung's candidate zone id when it is present AND the tz database resolves it; null otherwise,
    /// so the ladder carries on to the rung below.
    /// </summary>
    /// <remarks>
    /// An id this factory rejects is of no use to either consumer: the split count cannot be
    /// enumerated in it, and the outbound write resolves against the same tz database
    /// (<c>ITimeZoneService.IsValidZone</c>), so it would be rejected there too and fall back to the
    /// family's zone. Returning it would therefore buy nothing and cost the rungs below — and it must
    /// not be persisted either, or the next edit would resolve to the same dead end at rung 1.
    /// Reported at Debug, not Warning: the ladder handles it, and the one genuinely-guessing outcome
    /// (every rung exhausted) still announces itself through <see cref="LogFixedUtcFallback"/>.
    /// </remarks>
    private string? UsableZone(string? candidate, string seriesId)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        if (recurrenceTimeZoneFactory.TryCreate(candidate) is not null)
            return candidate;

        logger.LogDebug(
            "Series {SeriesId} offered the IANA time zone {IanaTimeZone}, which this tz database does not recognise; trying the next rung of the discovery ladder.",
            seriesId, candidate);
        return null;
    }

    // How many instances the ladder's rung 3 will probe before giving up. A master that 404s because
    // the series was genuinely deleted takes its instances with it, so every probe would 404 too —
    // bounded so that case costs a fixed handful of calls rather than one per synced occurrence.
    private const int MaxInstanceZoneProbes = 3;

    /// <summary>
    /// Ladder rung 3: ask Google for a surviving instance of this series and read its
    /// <c>start.timeZone</c>. Returns null when no probed instance yields one.
    /// </summary>
    /// <remarks>
    /// Ordinary instances are probed before exceptions: an exception can carry a zone of its own
    /// (an occurrence moved while travelling), which is not the zone the series is anchored to.
    /// A probe that fails is swallowed — the ladder has rungs below it, and Decision 3a is explicit
    /// that a user's edit is never failed over a zone lookup. Re-auth and cancellation still
    /// propagate: neither is a zone problem, and both must reach the caller.
    /// </remarks>
    private async Task<string?> FetchZoneFromSurvivingInstanceAsync(
        CalendarInfo owner, string seriesId, IReadOnlyList<CalendarEvent> seriesRows, CancellationToken ct)
    {
        var candidates = seriesRows
            .OrderBy(r => r.IsException)
            .ThenBy(r => r.Start)
            .Take(MaxInstanceZoneProbes);

        foreach (var candidate in candidates)
        {
            try
            {
                var detail = await googleCalendarClient.GetEventAsync(owner.GoogleCalendarId, candidate.GoogleEventId, ct);
                if (!string.IsNullOrWhiteSpace(detail?.IanaTimeZone))
                    return detail.IanaTimeZone;
            }
            catch (Exception ex) when (ex is not GoogleReauthRequiredException and not OperationCanceledException)
            {
                logger.LogDebug(
                    ex,
                    "Instance {GoogleEventId} of series {SeriesId} could not be fetched while resolving the series' time zone; trying the next candidate.",
                    candidate.GoogleEventId, seriesId);
            }
        }

        return null;
    }

    /// <summary>
    /// FHQ-164 Decision 4 — lazy backfill. A zone Google has just reported is written onto the
    /// series' stored rows, so the next edit resolves at rung 1 with no call at all.
    /// </summary>
    /// <remarks>
    /// There is no bulk migration and no schema default: rows stay null until Google actually
    /// reports a zone for them, and the ladder covers the gap meanwhile. This matters because normal
    /// sync would not otherwise backfill an EXISTING series' master —
    /// <c>CalendarSyncService</c> fetches one only when the RRULE is not already cached — though the
    /// instances of any series inside the sync window do get their zone from the list response.
    /// </remarks>
    private async Task PersistSeriesZoneAsync(
        IReadOnlyList<CalendarEvent> seriesRows, string ianaTimeZone, CancellationToken ct)
    {
        var backfilled = false;
        foreach (var row in seriesRows.Where(r => string.IsNullOrWhiteSpace(r.IanaTimeZone)))
        {
            row.IanaTimeZone = ianaTimeZone;
            await calendarRepository.UpdateEventAsync(row, ct);
            backfilled = true;
        }

        if (backfilled)
            await calendarRepository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The series' own zone as already stored on its local rows (ladder rung 1), or null.
    /// </summary>
    /// <remarks>
    /// An EXCEPTION instance can carry a different zone from its master, so ordinary instances are
    /// read first; an exception's zone is used only when there is nothing else, where it is still a
    /// Google-supplied value for this series and strictly better than a local setting.
    /// </remarks>
    private static string? StoredSeriesZone(IReadOnlyList<CalendarEvent> seriesRows) =>
        seriesRows.FirstOrDefault(r => !r.IsException && !string.IsNullOrWhiteSpace(r.IanaTimeZone))?.IanaTimeZone
        ?? seriesRows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.IanaTimeZone))?.IanaTimeZone;

    // FHQ-161: the zone the split count enumerates in. When no rung of the ladder yields a usable
    // zone we fall back DELIBERATELY to the legacy fixed-UTC enumeration rather than rejecting the
    // edit: a missing or unrecognised zone id must not fail a legitimate user edit, and the fallback
    // is no worse than the behaviour that shipped before this fix.
    private IRecurrenceTimeZone CreateRecurrenceZone(
        string seriesId, string? seriesZoneId, IReadOnlyList<CalendarEvent> seriesRows)
    {
        if (recurrenceTimeZoneFactory.TryCreate(seriesZoneId) is { } zone)
            return zone;

        LogFixedUtcFallback(seriesId, seriesZoneId, seriesRows);
        return FixedUtcRecurrenceTimeZone.Instance;
    }

    // The fixed-UTC fallback is only a PROBLEM for a timed series, where it is not DST-aware — that
    // is the one case worth a Warning, and it stays useful only if the benign case stays out of it
    // (logging standard: expected-and-handled conditions are not Warnings). The benign case is an
    // all-day series: it carries no start.timeZone by design and is date-anchored, so fixed-UTC
    // enumeration is EXACT for it, and it is the dominant real null case.
    //
    // FHQ-172 removed a second benign case, "the anchor degraded to a local row so it can carry no
    // zone". That is no longer reachable: this method is called only from ReshapeRuleAsync's COUNT
    // branch, which now refuses an unresolved anchor before the zone ladder runs at all. The
    // suppression is deleted rather than kept "just in case" — a branch guarding a condition that
    // cannot occur reads as evidence that it can.
    private void LogFixedUtcFallback(
        string seriesId, string? seriesZoneId, IReadOnlyList<CalendarEvent> seriesRows)
    {
        if (IsAllDaySeries(seriesRows))
        {
            logger.LogDebug(
                "Series {SeriesId} has no IANA time zone to count its COUNT split in ({TimeZoneId}); using fixed-UTC recurrence enumeration, which is exact for this series.",
                seriesId, seriesZoneId);
            return;
        }

        // FHQ-164 Decision 3a: with the discovery ladder in place this is the one genuinely-guessing
        // case left, so it announces itself by name rather than passing silently.
        logger.LogWarning(
            "Timed series {SeriesId} supplied no usable IANA time zone ({TimeZoneId}); counting the COUNT split with fixed-UTC recurrence enumeration, which is not DST-aware.",
            seriesId, seriesZoneId);
    }

    // Every instance of a series shares its master's all-day flag, so the local rows are a faithful
    // reading of it. No rows means nothing to read: treat the series as timed so the diagnostic is
    // not suppressed on a guess.
    private static bool IsAllDaySeries(IReadOnlyList<CalendarEvent> seriesRows) =>
        seriesRows.Count > 0 && seriesRows.All(r => r.IsAllDay);

    private async Task RemoveSeriesRowsFromSplitAsync(string seriesId, DateTimeOffset? splitFrom, CancellationToken ct)
    {
        var rows = await calendarRepository.GetEventsBySeriesIdAsync(seriesId, ct);
        var toRemove = splitFrom is { } from
            ? rows.Where(r => r.Start >= from)
            : rows;

        var removedAny = false;
        foreach (var row in toRemove)
        {
            await calendarRepository.DeleteEventAsync(row.Id, ct);
            removedAny = true;
        }

        if (removedAny)
            await calendarRepository.SaveChangesAsync(ct);
    }

    // Re-fetch the owner calendar's sync window from Google and upsert every instance by
    // GoogleEventId, recording an outbound-write hash for each so all N webhook echoes are
    // guarded (spec §10.2.2). Exception rows keep the overrides Google returns. Returns the
    // persisted rows (added or updated) so toggle callers can pick a return value and prune.
    private async Task<IReadOnlyList<CalendarEvent>> ReconcileWindowAsync(
        CalendarInfo owner, IReadOnlyDictionary<string, string> seriesRules, CancellationToken ct)
    {
        var persisted = new List<CalendarEvent>();
        var inserted = new List<CalendarEvent>();
        var syncState = await calendarRepository.GetSyncStateAsync(owner.Id, ct);
        if (syncState?.SyncWindowStart is not { } windowStart || syncState.SyncWindowEnd is not { } windowEnd)
            throw new InvalidOperationException(
                $"Cannot reconcile recurring write: calendar {owner.Id} has no stored sync window.");

        var (fetched, _) = await googleCalendarClient.GetEventsAsync(
            owner.GoogleCalendarId, windowStart, windowEnd, null, ct);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        // FHQ-47 (Gap 2): mirror CalendarSyncService — the free-form fallback resolves against
        // non-shared calendars only, while an explicit "[members: ...]" tag is authoritative and
        // resolves against ALL calendars, so a tagged member is not dropped while its calendar is
        // transiently shared (the first-login auto-designation window).
        var knownMemberNames = allCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();
        var allCalendarNames = allCalendars.Select(c => c.DisplayName).ToList();

        foreach (var fetchedEvent in fetched)
        {
            if (fetchedEvent.Title == "CANCELLED_TOMBSTONE")
            {
                var tombstoned = await calendarRepository.GetEventByGoogleEventIdAsync(fetchedEvent.GoogleEventId, ct);
                if (tombstoned != null)
                    await calendarRepository.DeleteEventAsync(tombstoned.Id, ct);
                continue;
            }

            var parsedNames = memberTagParser.ParseMembers(fetchedEvent.Description, knownMemberNames, allCalendarNames);
            var members = allCalendars
                .Where(c => parsedNames.Contains(c.DisplayName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            // No members parsed on an individual (non-shared) calendar → default to the owning
            // calendar so the instance is never stranded with an empty member set (mirrors
            // CalendarSyncService.SyncCoreAsync's memberless fallback).
            if (members.Count == 0 && !owner.IsShared)
                members.Add(owner);

            // GetEventsAsync only does pass-1, so fetched instances carry a null RecurrenceRule.
            // Stamp the rule this operation holds for the instance's own series so split/AllInSeries
            // instances are not persisted RRULE-less. Instances of OTHER series fall back to any
            // rule already stored locally and are never clobbered with this operation's rule.
            var seriesRule = fetchedEvent.GoogleRecurringEventId is { } sid
                ? seriesRules.GetValueOrDefault(sid)
                : null;

            var existing = await calendarRepository.GetEventByGoogleEventIdAsync(fetchedEvent.GoogleEventId, ct);
            if (existing != null)
            {
                existing.Title = fetchedEvent.Title;
                existing.Start = fetchedEvent.Start;
                existing.End = fetchedEvent.End;
                existing.IsAllDay = fetchedEvent.IsAllDay;
                existing.Location = fetchedEvent.Location;
                existing.Description = fetchedEvent.Description;
                existing.GoogleRecurringEventId = fetchedEvent.GoogleRecurringEventId;
                existing.OriginalStartTime = fetchedEvent.OriginalStartTime;
                existing.RecurrenceRule = fetchedEvent.RecurrenceRule ?? seriesRule ?? existing.RecurrenceRule;
                // FHQ-164 Decision 4: the reconcile is a Google fetch like any other, so it backfills
                // the anchor zone too. A BLANK value counts as absent, not as a new value — an
                // all-day event legitimately reports none, and storing "" would read as "no zone" to
                // the outbound write, which then re-anchors the series to the family's zone (FHQ-170).
                existing.IanaTimeZone = string.IsNullOrWhiteSpace(fetchedEvent.IanaTimeZone)
                    ? existing.IanaTimeZone
                    : fetchedEvent.IanaTimeZone;
                existing.Members = members;
                await calendarRepository.UpdateEventAsync(existing, ct);
                persisted.Add(existing);
            }
            else
            {
                fetchedEvent.OwnerCalendarInfoId = owner.Id;
                fetchedEvent.RecurrenceRule = fetchedEvent.RecurrenceRule ?? seriesRule;
                fetchedEvent.Members = members;
                await calendarRepository.AddEventAsync(fetchedEvent, ct);
                persisted.Add(fetchedEvent);
                inserted.Add(fetchedEvent);
            }

            // Record the hash Google will echo for this instance so its webhook is suppressed.
            // Google copies the MASTER's content-hash extended property onto every expanded
            // instance, so we must record that echoed value (surfaced on ContentHash by
            // GetEventsAsync) — a per-instance recompute would never match IsSelfEcho.
            if (!string.IsNullOrEmpty(fetchedEvent.ContentHash))
                RecordOutbound(fetchedEvent.GoogleEventId, fetchedEvent.ContentHash);
        }

        await SaveReconciledWithConcurrencyRetryAsync(inserted, ct);
        return persisted;
    }

    // The reconcile decides insert-vs-update with a check-then-insert (GetEventByGoogleEventIdAsync →
    // AddEventAsync) that races the single-consumer CalendarSyncWorker: when a sync of the same window
    // inserts the same GoogleEventId rows first, the batch SaveChanges trips the unique index
    // IX_Events_GoogleEventId (Postgres 23505) and the whole recurring write would 500 (FHQ-66). That
    // collision is benign convergence — both writers are persisting the same Google instances. Re-resolve
    // our inserts against the now-stored rows (first-writer-wins) and retry so the write is idempotent.
    // A failure that survives the retries is not this race and propagates.
    private const int MaxReconcileSaveAttempts = 5;

    private async Task SaveReconciledWithConcurrencyRetryAsync(IReadOnlyList<CalendarEvent> inserted, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await calendarRepository.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < MaxReconcileSaveAttempts)
            {
                logger.LogWarning(ex,
                    "Recurring reconcile write conflicted with a concurrent sync (attempt {Attempt}/{Max}); " +
                    "re-resolving {Count} inserted instance(s) against the stored rows and retrying.",
                    attempt, MaxReconcileSaveAttempts, inserted.Count);

                // Small linear backoff so the retries span past a concurrent sync's in-flight insert
                // burst (the conflict window is the sync's initial window population) rather than
                // hammering inside it.
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), ct);

                foreach (var insert in inserted)
                {
                    var stored = await calendarRepository.GetEventByGoogleEventIdAsync(insert.GoogleEventId, ct);
                    if (stored is null)
                        continue; // our insert is still the only writer for this id — leave it to the retry.

                    // The concurrent sync already created this row; drop our duplicate insert and fold our
                    // reconciled fields onto the stored row instead.
                    await calendarRepository.DetachEventAsync(insert, ct);
                    stored.Title = insert.Title;
                    stored.Start = insert.Start;
                    stored.End = insert.End;
                    stored.IsAllDay = insert.IsAllDay;
                    stored.Location = insert.Location;
                    stored.Description = insert.Description;
                    stored.GoogleRecurringEventId = insert.GoogleRecurringEventId;
                    stored.OriginalStartTime = insert.OriginalStartTime;
                    stored.RecurrenceRule = insert.RecurrenceRule ?? stored.RecurrenceRule;
                    // Blank counts as absent, exactly as in the reconcile above: "" would read as
                    // "no zone" to the outbound write and re-anchor the series (FHQ-170).
                    stored.IanaTimeZone = string.IsNullOrWhiteSpace(insert.IanaTimeZone)
                        ? stored.IanaTimeZone
                        : insert.IanaTimeZone;
                    stored.Members = insert.Members;
                    await calendarRepository.UpdateEventAsync(stored, ct);
                }
            }
        }
    }

    private static void ApplyRequestFields(CalendarEvent target, UpdateEventRequest request, string normalisedDescription)
    {
        target.Title = request.Title;
        target.Start = request.Start;
        target.End = request.End;
        target.IsAllDay = request.IsAllDay;
        target.Location = request.Location;
        target.Description = normalisedDescription;
    }

    private static string ComputeHash(CalendarEvent evt) =>
        EventContentHash.Compute(evt.Title, evt.Start, evt.End, evt.IsAllDay, evt.Description);

    /// <summary>
    /// FHQ-172: the content hash for a write that deliberately carries no start and no end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hash is stamped into <c>extendedProperties.private</c> and recorded so Google's echo of
    /// THIS write is recognised (FHQ-30). It therefore has to describe what was SENT. The only start
    /// in scope on this path is the local-row proxy — the one value here already known to be
    /// untrustworthy — so feeding it in would make the token encode a start Google never received,
    /// and would make two byte-identical title edits on the same series hash differently purely
    /// because a different local row happened to be the earliest one synced.
    /// </para>
    /// <para>
    /// This is a correctness-of-meaning fix rather than a bug fix for the echo guard. Nothing
    /// recomputes the hash from a fetched event: <c>CalendarSyncService.IsSelfEcho</c> compares the
    /// token Google echoes with the token this write recorded, so any stable string would suppress
    /// the echo and the proxy would not have broken it today. What it would break is the first time
    /// anyone reads the value as what its name says it is — a hash of the event's content.
    /// </para>
    /// <para>
    /// The omitted fields are represented by a fixed placeholder rather than dropped from the input,
    /// so the hash keeps <see cref="EventContentHash"/>'s single shape and its separator-collision
    /// guarantees. Which placeholder is irrelevant; that it is CONSTANT is the whole point.
    /// </para>
    /// <para>
    /// <b>The all-day flag is omitted too, for the same reason.</b> All-day-ness reaches Google ONLY
    /// through <c>start.date</c> versus <c>start.dateTime</c> — there is no separate field — so a
    /// write that sends neither key does not send the flag either, and hashing it would describe
    /// something that was not sent. Excluding it is safe because it cannot have changed on this
    /// path: <see cref="DescribeTimingChange"/> classifies an all-day flip as a timing change, and
    /// <see cref="PatchSeriesMasterAsync"/> refuses every timing change before reaching this write.
    /// So the flag on the resource is whatever Google already held, for every write hashed here, and
    /// a constant is a faithful description of that.
    /// </para>
    /// </remarks>
    private static string ComputeHashWithoutTimes(CalendarEvent evt) =>
        EventContentHash.Compute(evt.Title, TimesNotSent, TimesNotSent, AllDayNotSent, evt.Description);

    /// <summary>Placeholder standing for "this write sent no start/end" — see <see cref="ComputeHashWithoutTimes"/>.</summary>
    private static readonly DateTimeOffset TimesNotSent = DateTimeOffset.UnixEpoch;

    /// <summary>Placeholder standing for "this write sent no all-day flag" — see <see cref="ComputeHashWithoutTimes"/>.</summary>
    private const bool AllDayNotSent = false;

    private void RecordOutbound(string googleEventId, string hash)
    {
        outboundCache.Record(googleEventId, hash);
        logger.LogDebug("Recorded outbound write hash for event {EventId} (hash {Hash}).", googleEventId, hash);
    }
}
