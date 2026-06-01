using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Calendar.Recurrence;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Calendar;

public class CalendarEventService(
    IGoogleCalendarClient googleCalendarClient,
    ICalendarRepository calendarRepository,
    ICalendarMigrationService migrationService,
    IMemberTagParser memberTagParser,
    IOutboundWriteHashCache outboundCache,
    ILogger<CalendarEventService> logger) : ICalendarEventService
{
    public async Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default)
    {
        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var assignedMembers = request.MemberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new ArgumentException($"CalendarInfoId {id} is not known to the user.", nameof(request)))
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
            Members = assignedMembers
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

        logger.LogInformation("Event {GoogleEventId} created on calendar {CalendarId}.",
            calendarEvent.GoogleEventId, targetCalendar.GoogleCalendarId);

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

        logger.LogInformation("Recurring event {GoogleEventId} created on calendar {CalendarId}.",
            created.GoogleEventId, targetCalendar.GoogleCalendarId);

        // Return a persisted, reconciled recurring instance (consistent with the non-recurring path,
        // which returns the persisted row) rather than the unpersisted Google master object.
        return reconciled.FirstOrDefault(r => r.GoogleRecurringEventId == created.GoogleEventId)
            ?? reconciled.FirstOrDefault(r => r.IsRecurring)
            ?? created;
    }

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

        // Contradictory request: "clear recurrence" and "set a new rule" cannot both hold. Fail fast
        // rather than silently picking one (the two fields are mutually exclusive by contract).
        if (request.ClearRecurrence && request.RecurrenceRule is not null)
            throw new ArgumentException(
                "ClearRecurrence and RecurrenceRule are mutually exclusive: a single update cannot both remove and set a recurrence.",
                nameof(request));

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

        await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);

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
        await googleCalendarClient.UpdateEventAsync(owner.GoogleCalendarId, calendarEvent, hash, ct);
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
            throw new ArgumentException("At least one member is required.", nameof(memberCalendarInfoIds));

        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var calendarLookup = allCalendars.ToDictionary(c => c.Id);

        var newMembers = memberCalendarInfoIds
            .Select(id => calendarLookup.TryGetValue(id, out var cal)
                ? cal
                : throw new ArgumentException($"CalendarInfoId {id} is not known to the user.", nameof(memberCalendarInfoIds)))
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

            await googleCalendarClient.UpdateEventAsync(ownerCalendar.GoogleCalendarId, calendarEvent, hash, ct);

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
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

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
                // The master patch does not change the RRULE, so the series keeps its stored rule.
                seriesRules = SeriesRuleForExisting(calendarEvent);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown recurrence scope.");
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
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown recurrence scope.");
        }

        logger.LogInformation("Recurring event {EventId} deleted at scope {Scope}.", eventId, scope);
    }

    // ── Recurring edit/delete helpers ─────────────────────────────────────────

    private async Task<(CalendarEvent Event, CalendarInfo Owner)> LoadRecurringEventAsync(Guid eventId, CancellationToken ct)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        if (!calendarEvent.IsRecurring)
            throw new InvalidOperationException(
                $"Event {eventId} is not part of a recurring series. Use UpdateAsync/DeleteAsync for non-recurring events.");

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
            throw new InvalidOperationException(
                "Member changes apply to the whole series and are only permitted at the 'All events' scope.");
    }

    private async Task PatchInstanceAsync(
        CalendarEvent calendarEvent, CalendarInfo owner, UpdateEventRequest request, string normalisedDescription, CancellationToken ct)
    {
        // events.patch on the instance's OWN GoogleEventId — Google turns it into an exception.
        ApplyRequestFields(calendarEvent, request, normalisedDescription);

        var hash = ComputeHash(calendarEvent);
        await googleCalendarClient.UpdateEventAsync(owner.GoogleCalendarId, calendarEvent, hash, ct);
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
        // events.patch on the series master — Google preserves existing exceptions server-side.
        var master = new CalendarEvent
        {
            GoogleEventId = calendarEvent.GoogleRecurringEventId!,
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = normalisedDescription
        };

        var hash = ComputeHash(master);
        await googleCalendarClient.UpdateEventAsync(owner.GoogleCalendarId, master, hash, ct);
        RecordOutbound(master.GoogleEventId, hash);
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

        // (a) Truncate the original master: re-emit its RRULE with UNTIL = this instance's start − 1s.
        var truncatedRule = TruncateRuleBefore(calendarEvent);
        await googleCalendarClient.PatchSeriesRecurrenceAsync(owner.GoogleCalendarId, seriesId, truncatedRule, ct);

        // (b) Insert a NEW recurring series from this instance with the edited values and a fresh
        // RRULE shaped like the original (preserving its end condition — see ReshapeRule).
        var freshRule = await ReshapeRuleAsync(owner, seriesId, originalRule, seriesRows, calendarEvent.Start, ct);

        var newSeries = new CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = normalisedDescription
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
    // before the split, leaving the forward series too long (Major 3). If the master cannot be
    // resolved (404/transient), fall back to the earliest local row — the best-available proxy.
    private async Task<string> ReshapeRuleAsync(
        CalendarInfo owner, string seriesId, string rrule,
        IReadOnlyList<CalendarEvent> seriesRows, DateTimeOffset splitStart, CancellationToken ct)
    {
        var spec = RecurrenceRuleBuilder.ParseRRuleString(rrule);

        if (spec.End.Kind != RecurrenceEndKind.Count)
            return RecurrenceRuleBuilder.ToRRuleString(spec); // Never/Until preserved as-is.

        var anchor = await ResolveSeriesAnchorAsync(owner, seriesId, seriesRows, splitStart, ct);
        var before = RecurrenceRuleBuilder.CountOccurrencesBefore(spec, anchor, splitStart);
        var remaining = spec.End.Occurrences!.Value - before;

        if (remaining < 1)
            throw new InvalidOperationException(
                $"Cannot split a COUNT-based series: the split point leaves no occurrences for the " +
                $"forward series (original COUNT {spec.End.Occurrences}, {before} occurrences before the split).");

        return RecurrenceRuleBuilder.ToRRuleString(spec with { End = RecurrenceEnd.Count(remaining) });
    }

    // The true master DTSTART when the master is resolvable; otherwise the earliest local row, or the
    // split instant when no rows exist. A transient master-fetch failure degrades to the local proxy
    // rather than aborting the whole split.
    private async Task<DateTimeOffset> ResolveSeriesAnchorAsync(
        CalendarInfo owner, string seriesId, IReadOnlyList<CalendarEvent> seriesRows, DateTimeOffset splitStart, CancellationToken ct)
    {
        var localAnchor = seriesRows.Count > 0 ? seriesRows.Min(r => r.Start) : splitStart;

        var master = await googleCalendarClient.GetSeriesMasterAsync(owner.GoogleCalendarId, seriesId, ct);
        if (master is null)
        {
            logger.LogWarning(
                "Series master {SeriesId} on calendar {CalendarId} returned no start; anchoring COUNT split at the earliest local row instead.",
                seriesId, owner.GoogleCalendarId);
            return localAnchor;
        }

        return master.Start;
    }

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
            }

            // Record the hash Google will echo for this instance so its webhook is suppressed.
            // Google copies the MASTER's content-hash extended property onto every expanded
            // instance, so we must record that echoed value (surfaced on ContentHash by
            // GetEventsAsync) — a per-instance recompute would never match IsSelfEcho.
            if (!string.IsNullOrEmpty(fetchedEvent.ContentHash))
                RecordOutbound(fetchedEvent.GoogleEventId, fetchedEvent.ContentHash);
        }

        await calendarRepository.SaveChangesAsync(ct);
        return persisted;
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

    private void RecordOutbound(string googleEventId, string hash)
    {
        outboundCache.Record(googleEventId, hash);
        logger.LogDebug("Recorded outbound write hash for event {EventId} (hash {Hash}).", googleEventId, hash);
    }
}
