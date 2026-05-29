using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar.GoogleApi;

internal record GoogleApiEventDateTime(
    [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTime,
    [property: JsonPropertyName("date")]     string?         Date,
    [property: JsonPropertyName("timeZone")] string?         TimeZone);

internal record GoogleApiOrganizer(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("self")]  bool    Self);

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
    [property: JsonPropertyName("organizer")]            GoogleApiOrganizer?      Organizer,
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

internal record GoogleApiCalendarListEntry(
    [property: JsonPropertyName("id")]              string  Id,
    [property: JsonPropertyName("summary")]         string? Summary,
    [property: JsonPropertyName("summaryOverride")] string? SummaryOverride,
    [property: JsonPropertyName("backgroundColor")] string? BackgroundColor,
    [property: JsonPropertyName("foregroundColor")] string? ForegroundColor,
    [property: JsonPropertyName("accessRole")]      string? AccessRole);

internal record GoogleApiCalendarList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiCalendarListEntry> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);

internal record GoogleApiWatchResponse(
    [property: JsonPropertyName("id")]         string Id,
    [property: JsonPropertyName("resourceId")] string ResourceId,
    [property: JsonPropertyName("expiration")] long   Expiration);
