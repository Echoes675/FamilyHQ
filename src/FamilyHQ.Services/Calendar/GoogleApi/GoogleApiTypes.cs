using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar.GoogleApi;

// FHQ-174 — assessed and deliberately left as DateTimeOffset. System.Text.Json assumes the HOST's
// offset for an ISO-8601 value that carries none, which is the same substitution GoogleAllDayDate
// exists to remove. It is inert here and stays that way for a reason, not by luck: Google's
// `dateTime` is RFC 3339 and always carries an offset, and the Simulator formats every one of its
// own from a Kind=Utc DateTime (`ToString("O")` → trailing 'Z'). Binding it as a string and parsing
// by hand would trade an inert hazard for a live one — a hand-rolled ParseExact would reject the
// legitimate RFC 3339 variations (fractional seconds, ±hh:mm offsets) that the framework parser
// already handles, and it would touch every read site. The contract is pinned instead by
// GoogleAllDayDateContractTests, which asserts an offset-carrying `dateTime` lands on the exact
// instant Google named. An offset-LESS `dateTime` is not defended: Google does not send one, and if
// it ever did, `timeZone` — not the host's zone — would be the only defensible interpretation, so
// guessing here would be wrong in a new way rather than right.
internal record GoogleApiEventDateTime(
    [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTime,
    [property: JsonPropertyName("date")]     string?         Date,
    [property: JsonPropertyName("timeZone")] string?         TimeZone);

// FHQ-166: there is deliberately no `organizer` binding. Google sends one on every event and its
// `email` is a live address; binding it would materialise that address in memory on every fetch for
// nothing, since no caller reads it. System.Text.Json ignores JSON members with no corresponding
// property, so the field simply never leaves the response stream.
internal record GoogleApiPrivateExtendedProperties(
    [property: JsonPropertyName("content-hash")] string? ContentHash);

internal record GoogleApiExtendedProperties(
    // FamilyHQ only writes to extendedProperties.private; shared namespace intentionally omitted.
    [property: JsonPropertyName("private")] GoogleApiPrivateExtendedProperties? Private);

internal record GoogleApiEvent(
    [property: JsonPropertyName("id")]                   string                   Id,
    [property: JsonPropertyName("iCalUID")]              string?                  ICalUID,
    [property: JsonPropertyName("status")]               string?                  Status,
    [property: JsonPropertyName("summary")]              string?                  Summary,
    [property: JsonPropertyName("description")]          string?                  Description,
    [property: JsonPropertyName("location")]             string?                  Location,
    [property: JsonPropertyName("start")]                GoogleApiEventDateTime?  Start,
    [property: JsonPropertyName("end")]                  GoogleApiEventDateTime?  End,
    [property: JsonPropertyName("extendedProperties")]   GoogleApiExtendedProperties? ExtendedProperties,
    // Recurring-series metadata. recurringEventId links an instance to its series master;
    // originalStartTime is set only on exception instances (moved/modified occurrences);
    // recurrence carries the RRULE/EXDATE/RDATE lines and is present only on the master.
    [property: JsonPropertyName("recurringEventId")]     string?                  RecurringEventId = null,
    [property: JsonPropertyName("originalStartTime")]    GoogleApiEventDateTime?  OriginalStartTime = null,
    [property: JsonPropertyName("recurrence")]           List<string>?            Recurrence = null);

internal record GoogleApiEventList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiEvent> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);

// FHQ-164: `timeZone` is the calendar's own default zone — the zone Google applies to an event on
// this calendar that carries none of its own. Marked optional on Google's calendar resource, which
// is why the discovery ladder states a terminal behaviour rather than assuming it is always present.
internal record GoogleApiCalendarListEntry(
    [property: JsonPropertyName("id")]              string  Id,
    [property: JsonPropertyName("summary")]         string? Summary,
    [property: JsonPropertyName("summaryOverride")] string? SummaryOverride,
    [property: JsonPropertyName("backgroundColor")] string? BackgroundColor,
    [property: JsonPropertyName("foregroundColor")] string? ForegroundColor,
    [property: JsonPropertyName("accessRole")]      string? AccessRole,
    [property: JsonPropertyName("timeZone")]        string? TimeZone = null);

internal record GoogleApiCalendarList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiCalendarListEntry> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);

internal record GoogleApiWatchResponse(
    [property: JsonPropertyName("id")]         string Id,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("expiration")] long   Expiration);
