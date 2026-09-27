using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
using Xunit;

namespace FamilyHQ.Core.Tests.Validators;

/// <summary>
/// The reminder rules apply to values the kiosk created and to nothing else. A request that carries
/// no reminders is not making a reminder change, so none of these rules may fire on it — and a value
/// Google supplied never reaches this validator at all, because Google is the authority on its own
/// data and "correcting" what it sent is the same class of bug as overwriting a time zone.
/// </summary>
public class EventRemindersValidationTests
{
    private static CreateEventRequest Create(EventReminders? reminders) =>
        new([Guid.NewGuid()], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, null, null, reminders);

    private static UpdateEventRequest Update(EventReminders? reminders) =>
        new("Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null,
            null, false, reminders);

    private static async Task<bool> CreateIsValidAsync(EventReminders? reminders) =>
        (await new CreateEventRequestValidator().ValidateAsync(Create(reminders))).IsValid;

    private static async Task<bool> UpdateIsValidAsync(EventReminders? reminders) =>
        (await new UpdateEventRequestValidator().ValidateAsync(Update(reminders))).IsValid;

    private static EventReminders Overrides(params EventReminder[] overrides) =>
        EventReminders.Explicit(overrides);

    private static EventReminder[] Popups(int count) =>
        Enumerable.Range(1, count).Select(i => new EventReminder("popup", i * 5)).ToArray();

    [Fact]
    public async Task AbsentReminders_IsValid_BecauseTheRequestIsNotAReminderChange()
    {
        (await CreateIsValidAsync(null)).Should().BeTrue();
        (await UpdateIsValidAsync(null)).Should().BeTrue();
    }

    [Fact]
    public async Task FiveOverrides_IsValid()
    {
        (await CreateIsValidAsync(Overrides(Popups(5)))).Should().BeTrue();
        (await UpdateIsValidAsync(Overrides(Popups(5)))).Should().BeTrue();
    }

    [Fact]
    public async Task SixOverrides_IsInvalid()
    {
        // Google rejects the sixth outright — 400 eventRemindersCountExceedsLimit.
        (await CreateIsValidAsync(Overrides(Popups(6)))).Should().BeFalse();
        (await UpdateIsValidAsync(Overrides(Popups(6)))).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-540)]
    [InlineData(40321)]
    public async Task MinutesOutsideGooglesRange_IsInvalid(int minutes)
    {
        // Google accepts these with a 200 and silently clamps them, so the kiosk must refuse them
        // up front rather than show the family a reminder time Google never stored.
        (await CreateIsValidAsync(Overrides(new EventReminder("popup", minutes)))).Should().BeFalse();
        (await UpdateIsValidAsync(Overrides(new EventReminder("popup", minutes)))).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40320)]
    public async Task MinutesAtTheBoundaries_IsValid(int minutes)
    {
        (await CreateIsValidAsync(Overrides(new EventReminder("popup", minutes)))).Should().BeTrue();
        (await UpdateIsValidAsync(Overrides(new EventReminder("popup", minutes)))).Should().BeTrue();
    }

    [Theory]
    [InlineData("sms")]
    [InlineData("POPUP")]
    [InlineData("")]
    public async Task MethodOtherThanPopupOrEmail_IsInvalid(string method)
    {
        // Google drops an unknown method with a 200, leaving the event with no reminder at all.
        (await CreateIsValidAsync(Overrides(new EventReminder(method, 10)))).Should().BeFalse();
        (await UpdateIsValidAsync(Overrides(new EventReminder(method, 10)))).Should().BeFalse();
    }

    [Theory]
    [InlineData("popup")]
    [InlineData("email")]
    public async Task TheTwoMethodsGoogleOffers_AreValid(string method)
    {
        (await CreateIsValidAsync(Overrides(new EventReminder(method, 10)))).Should().BeTrue();
        (await UpdateIsValidAsync(Overrides(new EventReminder(method, 10)))).Should().BeTrue();
    }

    [Fact]
    public async Task UseDefaultWithOverrides_IsInvalid_BecauseTheyAreMutuallyExclusive()
    {
        var contradictory = new EventReminders
        {
            UseDefault = true,
            Overrides = new List<EventReminder> { new("popup", 10) }
        };

        (await CreateIsValidAsync(contradictory)).Should().BeFalse();
        (await UpdateIsValidAsync(contradictory)).Should().BeFalse();
    }

    [Fact]
    public async Task UseDefaultWithNoOverrides_IsValid_BecauseThatIsTheRevertToDefaultShape()
    {
        (await CreateIsValidAsync(EventReminders.InheritsCalendarDefault)).Should().BeTrue();
        (await UpdateIsValidAsync(EventReminders.InheritsCalendarDefault)).Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitlyNone_IsValid()
    {
        (await CreateIsValidAsync(EventReminders.ExplicitlyNone)).Should().BeTrue();
        (await UpdateIsValidAsync(EventReminders.ExplicitlyNone)).Should().BeTrue();
    }
}
