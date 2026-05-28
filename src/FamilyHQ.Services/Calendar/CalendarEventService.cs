using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar.Recurrence;
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

    public async Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default)
    {
        var calendarEvent = await calendarRepository.GetEventAsync(eventId, ct)
            ?? throw new InvalidOperationException($"Event {eventId} not found.");

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var ownerCalendar = allCalendars.FirstOrDefault(c => c.Id == calendarEvent.OwnerCalendarInfoId)
            ?? throw new InvalidOperationException($"Owner calendar {calendarEvent.OwnerCalendarInfoId} not found for event {eventId}.");

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
                // Fail fast on COUNT-bounded series before any Google mutation (FHQ-18.5 limitation).
                RejectCountBasedThisAndFollowing(calendarEvent);
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

        // Fail fast on COUNT-bounded series before any Google mutation (FHQ-18.5 limitation).
        RejectCountBasedThisAndFollowing(calendarEvent);

        // (a) Truncate the original master: re-emit its RRULE with UNTIL = this instance's start − 1s.
        var truncatedRule = TruncateRuleBefore(calendarEvent);
        await googleCalendarClient.PatchSeriesRecurrenceAsync(owner.GoogleCalendarId, seriesId, truncatedRule, ct);

        // (b) Insert a NEW recurring series from this instance with the edited values and a fresh
        // RRULE shaped like the original (preserving its end condition — see ReshapeRule).
        var freshRule = ReshapeRule(calendarEvent.RecurrenceRule
            ?? throw new InvalidOperationException($"Recurring event {calendarEvent.Id} has no stored RecurrenceRule to split."));

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
    // series stays Never. A COUNT series is rejected upstream by RejectCountBasedThisAndFollowing
    // because computing the remaining occurrence count needs expansion that lands in FHQ-18.5.
    private static string ReshapeRule(string rrule)
    {
        var spec = RecurrenceRuleBuilder.ParseRRuleString(rrule);
        return RecurrenceRuleBuilder.ToRRuleString(spec); // End (Never/Until) is preserved as-is.
    }

    // ThisAndFollowing on a COUNT-bounded series would require expanding occurrences to compute the
    // remaining count for the new forward series — not available until FHQ-18.5. Reject it loudly
    // rather than silently corrupting the series end on Google (a reshaped COUNT would either run
    // forever or restart the count from the split point).
    private static void RejectCountBasedThisAndFollowing(CalendarEvent calendarEvent)
    {
        var rrule = calendarEvent.RecurrenceRule
            ?? throw new InvalidOperationException(
                $"Recurring event {calendarEvent.Id} has no stored RecurrenceRule to split.");

        if (RecurrenceRuleBuilder.ParseRRuleString(rrule).End.Kind == RecurrenceEndKind.Count)
            throw new InvalidOperationException(
                "'This and following' is not supported on a COUNT-based recurring series yet: " +
                "computing the remaining occurrence count requires occurrence expansion (FHQ-18.5). " +
                "Edit the whole series instead, or wait for FHQ-18.5.");
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
    // guarded (spec §10.2.2). Exception rows keep the overrides Google returns.
    private async Task ReconcileWindowAsync(
        CalendarInfo owner, IReadOnlyDictionary<string, string> seriesRules, CancellationToken ct)
    {
        var syncState = await calendarRepository.GetSyncStateAsync(owner.Id, ct);
        if (syncState?.SyncWindowStart is not { } windowStart || syncState.SyncWindowEnd is not { } windowEnd)
            throw new InvalidOperationException(
                $"Cannot reconcile recurring write: calendar {owner.Id} has no stored sync window.");

        var (fetched, _) = await googleCalendarClient.GetEventsAsync(
            owner.GoogleCalendarId, windowStart, windowEnd, null, ct);

        var allCalendars = await calendarRepository.GetCalendarsAsync(ct);
        var knownMemberNames = allCalendars.Where(c => !c.IsShared).Select(c => c.DisplayName).ToList();

        foreach (var fetchedEvent in fetched)
        {
            if (fetchedEvent.Title == "CANCELLED_TOMBSTONE")
            {
                var tombstoned = await calendarRepository.GetEventByGoogleEventIdAsync(fetchedEvent.GoogleEventId, ct);
                if (tombstoned != null)
                    await calendarRepository.DeleteEventAsync(tombstoned.Id, ct);
                continue;
            }

            var parsedNames = memberTagParser.ParseMembers(fetchedEvent.Description, knownMemberNames);
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
            }
            else
            {
                fetchedEvent.OwnerCalendarInfoId = owner.Id;
                fetchedEvent.RecurrenceRule = fetchedEvent.RecurrenceRule ?? seriesRule;
                fetchedEvent.Members = members;
                await calendarRepository.AddEventAsync(fetchedEvent, ct);
            }

            // Record the hash Google will echo for this instance so its webhook is suppressed.
            // Google copies the MASTER's content-hash extended property onto every expanded
            // instance, so we must record that echoed value (surfaced on ContentHash by
            // GetEventsAsync) — a per-instance recompute would never match IsSelfEcho.
            if (!string.IsNullOrEmpty(fetchedEvent.ContentHash))
                RecordOutbound(fetchedEvent.GoogleEventId, fetchedEvent.ContentHash);
        }

        await calendarRepository.SaveChangesAsync(ct);
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
