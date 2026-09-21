using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Models;
using FluentAssertions;
using Moq;
using Moq.Protected;
using static FamilyHQ.Services.Tests.Calendar.GoogleCalendarClientMappingTests;

namespace FamilyHQ.Services.Tests.Calendar;

// FHQ-189: the read path, proven against the REAL Google payloads the FHQ-193 spike captured
// (Done/FHQ-193/fixtures). Every payload below is a real response body, not an invention.
public class GoogleCalendarClientRemindersTests
{
    // Helper: stub the events.list response with the given items array and return the mapped events.
    // Mirror CreateSut()/SetupAuthResponse() from GoogleCalendarClientMappingTests.
    private static async Task<IReadOnlyList<CalendarEvent>> MapEventsAsync(string itemsJson)
    {
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/events")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent($$"""{"items": {{itemsJson}}}""")
            });

        var (events, _) = await sut.GetEventsAsync("cal-1", null, null, "token", CancellationToken.None);
        return events.ToList();
    }

    [Fact]
    public async Task TimedEventUsingCalendarDefault_MapsToInheritsCalendarDefault()
    {
        // fixture 10-T1-timed-nokey.json
        var events = await MapEventsAsync("""
            [{"id":"e1","summary":"T","start":{"dateTime":"2027-01-12T10:00:00Z"},"end":{"dateTime":"2027-01-12T11:00:00Z"},
              "reminders":{"useDefault":true}}]
            """);

        events.Should().ContainSingle();
        events[0].Reminders!.SameAs(EventReminders.InheritsCalendarDefault).Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitOverrides_MapInFull()
    {
        // fixture 11-T2-timed-overrides.json
        var events = await MapEventsAsync("""
            [{"id":"e2","summary":"T","start":{"dateTime":"2027-01-12T14:00:00Z"},"end":{"dateTime":"2027-01-12T15:00:00Z"},
              "reminders":{"useDefault":false,"overrides":[{"method":"popup","minutes":47},{"method":"email","minutes":1440}]}}]
            """);

        events[0].Reminders!.UseDefault.Should().BeFalse();
        events[0].Reminders!.Overrides.Should().BeEquivalentTo(
            [new EventReminder("popup", 47), new EventReminder("email", 1440)]);
    }

    [Fact]
    public async Task UseDefaultFalseWithNoOverridesKey_MapsToExplicitlyNone_NotNull()
    {
        // fixture 33-T2-explicit-none.json — Google omits the overrides key entirely.
        // A missing array is EMPTY, not unknown: mapping it to null would make an event that has
        // been synced indistinguishable from one that has not, and would re-trigger the backfill.
        var events = await MapEventsAsync("""
            [{"id":"e3","summary":"T","start":{"dateTime":"2027-01-12T10:00:00Z"},"end":{"dateTime":"2027-01-12T11:00:00Z"},
              "reminders":{"useDefault":false}}]
            """);

        events[0].Reminders.Should().NotBeNull();
        events[0].Reminders!.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public async Task NoRemindersObjectAtAll_MapsToNull()
    {
        // Google always sends `reminders` on a real event, but a tombstone or a trimmed payload
        // may not. Null means "not synced" and is what drives the backfill.
        var events = await MapEventsAsync("""
            [{"id":"e4","summary":"T","start":{"dateTime":"2027-01-12T10:00:00Z"},"end":{"dateTime":"2027-01-12T11:00:00Z"}}]
            """);

        events[0].Reminders.Should().BeNull();
    }

    [Fact]
    public async Task ValuesTheKioskCouldNotHaveCreated_RoundTripUnchanged()
    {
        // The read path never validates, clamps, de-duplicates or reorders.
        var events = await MapEventsAsync("""
            [{"id":"e5","summary":"T","start":{"dateTime":"2027-01-12T10:00:00Z"},"end":{"dateTime":"2027-01-12T11:00:00Z"},
              "reminders":{"useDefault":false,"overrides":[{"method":"sms","minutes":-540},{"method":"popup","minutes":99999},{"method":"popup","minutes":10},{"method":"popup","minutes":10}]}}]
            """);

        events[0].Reminders!.Overrides.Should().BeEquivalentTo([
            new EventReminder("sms", -540),
            new EventReminder("popup", 99999),
            new EventReminder("popup", 10),
            new EventReminder("popup", 10)
        ]);
    }

    [Fact]
    public async Task AllDayEventCarriesMaterialisedDefault()
    {
        // fixture 12-A1-allday-nokey.json — an all-day event NEVER inherits. Google copies the
        // calendar's default in as explicit overrides at creation.
        var events = await MapEventsAsync("""
            [{"id":"e6","summary":"T","start":{"date":"2027-01-13"},"end":{"date":"2027-01-14"},
              "reminders":{"useDefault":false,"overrides":[{"method":"popup","minutes":30}]}}]
            """);

        events[0].IsAllDay.Should().BeTrue();
        events[0].Reminders!.UseDefault.Should().BeFalse();
        events[0].Reminders!.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(30);
    }

    // ── I1: GetCalendarsAsync's DefaultReminders mapping ─────────────────────────────────
    // The plan never covered this mapping with a test. Real payload shapes are in
    // D:\Obsidian Vault\FamilyHQ\Done\FHQ-193\fixtures\00-calendarlist-primary.json and
    // 60-calendarlist.json — "present" and "empty array" below are taken from those fixtures.

    private static async Task<IReadOnlyList<CalendarInfo>> MapCalendarsAsync(string itemsJson)
    {
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent($$"""{"items": {{itemsJson}}}""")
            });

        var result = await sut.GetCalendarsAsync(CancellationToken.None);
        return result.ToList();
    }

    [Fact]
    public async Task DefaultReminders_Present_MapsToExplicitOverrides()
    {
        // fixture 00-calendarlist-primary.json / 60-calendarlist.json (primary entry).
        var calendars = await MapCalendarsAsync("""
            [{"id":"cal-1","summary":"Primary","defaultReminders":[{"method":"popup","minutes":30}]}]
            """);

        calendars.Should().ContainSingle();
        calendars[0].DefaultReminders!.SameAs(EventReminders.Explicit([new EventReminder("popup", 30)]))
            .Should().BeTrue();
    }

    [Fact]
    public async Task DefaultReminders_AbsentKey_MapsToNull()
    {
        // Google is not guaranteed to send defaultReminders at all (it is optional on the resource).
        var calendars = await MapCalendarsAsync("""
            [{"id":"cal-1","summary":"Primary"}]
            """);

        calendars[0].DefaultReminders.Should().BeNull();
    }

    [Fact]
    public async Task DefaultReminders_EmptyArray_MapsToExplicitlyNoneShape_NotNull()
    {
        // fixture 60-calendarlist.json — "Holidays in United Kingdom" / "Work" / "Personal" entries.
        // An empty array is a real answer ("no default reminders"), not "unknown" — must not collapse
        // to null, which would mean "never synced".
        var calendars = await MapCalendarsAsync("""
            [{"id":"cal-1","summary":"Work","defaultReminders":[]}]
            """);

        calendars[0].DefaultReminders.Should().NotBeNull();
        calendars[0].DefaultReminders!.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public async Task DefaultReminders_EntryMissingMethodOrMinutes_IsSkippedNotDefaulted()
    {
        // Same no-invention rule as the per-event override mapping: an entry Google could not have
        // sent from a real UI is dropped rather than defaulted.
        var calendars = await MapCalendarsAsync("""
            [{"id":"cal-1","summary":"Primary","defaultReminders":[
                {"method":"popup","minutes":30},
                {"method":"email"},
                {"minutes":15}
            ]}]
            """);

        calendars[0].DefaultReminders!.Overrides.Should().ContainSingle()
            .Which.Should().Be(new EventReminder("popup", 30));
    }

    [Fact]
    public async Task EventsListRequest_AsksGoogleForTheRemindersField()
    {
        // Without `reminders` in the fields mask Google sends none at all, and every test above
        // would pass against a payload production never receives.
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        string? requestedUrl = null;
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString().Contains("/events")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((r, _) => requestedUrl = r.RequestUri!.ToString())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("""{"items":[]}""") });

        await sut.GetEventsAsync("cal-1", null, null, "token", CancellationToken.None);

        Uri.UnescapeDataString(requestedUrl!).Should().Contain("reminders");
    }
}
