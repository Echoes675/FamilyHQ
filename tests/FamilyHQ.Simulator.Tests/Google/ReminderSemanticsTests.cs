using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Google;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Google;

/// <summary>
/// Google's real reminder behaviour, captured from live API responses. Several of these are
/// surprising: Google accepts nonsense and silently rewrites it rather than rejecting it, so a
/// simulator that rejected the same input would let the kiosk's tests assert behaviour Google does
/// not have.
/// </summary>
public class ReminderSemanticsTests
{
    private static GoogleEventReminders Explicit(params GoogleEventReminderOverride[] overrides) =>
        new(UseDefault: false, Overrides: [.. overrides]);

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_WithFiveOverrides_IsAccepted()
    {
        var result = ReminderSemantics.Validate(Explicit(
            new("popup", 5), new("popup", 10), new("popup", 15), new("popup", 20), new("email", 25)));

        result.Should().BeNull("five is Google's limit, not four");
    }

    [Fact]
    public void Validate_WithSixOverrides_ReturnsGooglesCountReason()
    {
        var result = ReminderSemantics.Validate(Explicit(
            new("popup", 5), new("popup", 10), new("popup", 15),
            new("popup", 20), new("popup", 25), new("popup", 30)));

        result.Should().Be("eventRemindersCountExceedsLimit");
    }

    [Fact]
    public void Validate_CountsTheRequestAsSent_NotAfterDeduplication()
    {
        // Six overrides of which two are identical. Google rejects on the count it was SENT; it does
        // not de-duplicate first and then find five. Validating after normalisation would wrongly
        // accept this.
        var result = ReminderSemantics.Validate(Explicit(
            new("popup", 5), new("popup", 5), new("popup", 10),
            new("popup", 15), new("popup", 20), new("popup", 25)));

        result.Should().Be("eventRemindersCountExceedsLimit");
    }

    [Fact]
    public void Validate_WithUseDefaultAndOverrides_ReturnsGooglesConflictReason()
    {
        var result = ReminderSemantics.Validate(
            new(UseDefault: true, Overrides: [new("popup", 30)]));

        result.Should().Be("cannotUseDefaultRemindersAndSpecifyOverride");
    }

    [Fact]
    public void Validate_WithUseDefaultAndEmptyOverrides_IsAccepted()
    {
        // This exact body is how a client reverts an event to the calendar default. useDefault:true
        // on its own is rejected by Google, so the empty array is load-bearing.
        ReminderSemantics.Validate(new(UseDefault: true, Overrides: [])).Should().BeNull();
    }

    [Fact]
    public void Validate_WithNoReminders_IsAccepted()
    {
        ReminderSemantics.Validate(null).Should().BeNull();
    }

    // ── Silent rewrites: Google returns 200 and stores something else ──────────

    [Fact]
    public void NormaliseForStorage_ClampsNegativeMinutesToZero()
    {
        // A negative minutes value means "after the event starts", which Google's API cannot express.
        // It does not reject it — it clamps to 0, which is a reminder at the event's start.
        var result = ReminderSemantics.NormaliseForStorage(Explicit(new GoogleEventReminderOverride("popup", -540)));

        result!.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(0);
    }

    [Fact]
    public void NormaliseForStorage_ClampsMinutesAboveFourWeeksToTheMaximum()
    {
        var result = ReminderSemantics.NormaliseForStorage(Explicit(new GoogleEventReminderOverride("popup", 40321)));

        result!.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(40320);
    }

    [Fact]
    public void NormaliseForStorage_RemovesDuplicates()
    {
        var result = ReminderSemantics.NormaliseForStorage(Explicit(
            new("popup", 30), new("popup", 30), new("email", 30)));

        result!.Overrides.Should().HaveCount(2, "same method and minutes is one reminder to Google");
    }

    [Fact]
    public void NormaliseForStorage_DropsAnUnknownMethodEntirely()
    {
        // The dangerous one: asking for an sms reminder leaves the event with NO reminder at all
        // rather than an error. A caller that assumed success would be wrong and never told.
        var result = ReminderSemantics.NormaliseForStorage(Explicit(new GoogleEventReminderOverride("sms", 30)));

        result!.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void NormaliseForStorage_KeepsTheSurvivorsWhenOnlySomeAreDropped()
    {
        var result = ReminderSemantics.NormaliseForStorage(Explicit(
            new("sms", 30), new("popup", 10), new("email", 60)));

        result!.Overrides.Should().HaveCount(2);
        result.Overrides.Should().OnlyContain(o => o.Method == "popup" || o.Method == "email");
    }

    [Fact]
    public void NormaliseForStorage_ClampsBeforeDeduplicating()
    {
        // Two different out-of-range values that clamp onto the same reminder are one reminder once
        // stored, so the clamp has to happen first.
        var result = ReminderSemantics.NormaliseForStorage(Explicit(
            new("popup", -5), new("popup", -60)));

        result!.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(0);
    }

    // ── Read shaping ──────────────────────────────────────────────────────────

    [Fact]
    public void ShapeForRead_WithNothingStoredOnATimedEvent_ReportsUseDefault()
    {
        var result = ReminderSemantics.ShapeForRead(stored: null, isAllDay: false, calendarDefaults: null);

        result.UseDefault.Should().BeTrue();
        result.Overrides.Should().BeNull("Google omits the key rather than sending an empty array");
    }

    [Fact]
    public void ShapeForRead_WithNothingStoredOnAnAllDayEvent_MaterialisesTheCalendarDefaults()
    {
        // An all-day event never inherits. Google copies the calendar's defaults onto it as explicit
        // overrides at creation, so useDefault:true simply does not occur on all-day events.
        var result = ReminderSemantics.ShapeForRead(
            stored: null,
            isAllDay: true,
            calendarDefaults: [new("popup", 30)]);

        result.UseDefault.Should().BeFalse();
        result.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(30);
    }

    [Fact]
    public void ShapeForRead_WithNothingStoredOnAnAllDayEventAndNoCalendarDefaults_ReportsNone()
    {
        var result = ReminderSemantics.ShapeForRead(stored: null, isAllDay: true, calendarDefaults: null);

        result.UseDefault.Should().BeFalse();
        result.Overrides.Should().BeNull();
    }

    [Fact]
    public void ShapeForRead_WithExplicitlyNoneStored_OmitsTheOverridesKey()
    {
        // Stored as useDefault:false with an empty list; Google reads it back with no overrides key at
        // all. A client must treat a missing array as empty, not as unknown.
        var result = ReminderSemantics.ShapeForRead(Explicit(), isAllDay: false, calendarDefaults: null);

        result.UseDefault.Should().BeFalse();
        result.Overrides.Should().BeNull();
    }

    [Fact]
    public void ShapeForRead_DoesNotPreserveTheOrderTheOverridesWereSentIn()
    {
        // Google reorders. The simulator reorders deterministically so the test suite is stable while
        // still refusing to reward code that depends on send order.
        var result = ReminderSemantics.ShapeForRead(
            Explicit(new("popup", 10), new("popup", 60)),
            isAllDay: false,
            calendarDefaults: null);

        result.Overrides!.Select(o => o.Minutes).Should().NotEqual(new int?[] { 10, 60 });
        result.Overrides!.Select(o => o.Minutes).Should().BeEquivalentTo(new int?[] { 10, 60 });
    }
}
