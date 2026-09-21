using FamilyHQ.Core.Models;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Models;

// FHQ-189: the shape Google's `reminders` object is stored in. Proven against real API responses
// captured by the FHQ-193 spike (Done/FHQ-193/fixtures) — in particular that Google REORDERS
// overrides, so equality here must be a set comparison, and that the read path must never
// normalise a value Google sent.
public class EventRemindersTests
{
    [Fact]
    public void Overrides_DefaultsToEmpty_NeverNull()
    {
        new EventReminders().Overrides.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void InheritsCalendarDefault_IsUseDefaultWithNoOverrides()
    {
        EventReminders.InheritsCalendarDefault.UseDefault.Should().BeTrue();
        EventReminders.InheritsCalendarDefault.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitlyNone_IsNotUseDefaultAndHasNoOverrides()
    {
        // Google returns `{"useDefault":false}` with NO overrides key for this state
        // (fixture 33-T2-explicit-none.json). It is distinct from InheritsCalendarDefault:
        // collapsing the two would write explicit-none where Google had inheritance.
        EventReminders.ExplicitlyNone.UseDefault.Should().BeFalse();
        EventReminders.ExplicitlyNone.Overrides.Should().BeEmpty();
        EventReminders.ExplicitlyNone.SameAs(EventReminders.InheritsCalendarDefault).Should().BeFalse();
    }

    [Fact]
    public void SameAs_IgnoresOrder()
    {
        // Sent [10, 60]; Google returned [60, 10] — fixture 25-T2-patch-overrides-only.json.
        var sent = EventReminders.Explicit([new("popup", 10), new("popup", 60)]);
        var returned = EventReminders.Explicit([new("popup", 60), new("popup", 10)]);

        sent.SameAs(returned).Should().BeTrue();
    }

    [Fact]
    public void SameAs_CountsDuplicates()
    {
        var one = EventReminders.Explicit([new("popup", 10)]);
        var two = EventReminders.Explicit([new("popup", 10), new("popup", 10)]);

        one.SameAs(two).Should().BeFalse();

        // Deferred minor #1: the pair above differs in Overrides.Count, so SameAs never reaches the
        // multiset comparison — it is short-circuited by the count check above it. Same COUNT,
        // different DISTRIBUTION is the case that actually exercises the multiset logic: a naive
        // "same distinct values" comparison would wrongly call these equal (both contain 10 and 20).
        var twoTens = EventReminders.Explicit([new("popup", 10), new("popup", 10), new("popup", 20)]);
        var twoTwenties = EventReminders.Explicit([new("popup", 10), new("popup", 20), new("popup", 20)]);

        twoTens.SameAs(twoTwenties).Should().BeFalse();
    }

    [Fact]
    public void SameAs_DistinguishesMethod()
    {
        EventReminders.Explicit([new("popup", 10)])
            .SameAs(EventReminders.Explicit([new("email", 10)]))
            .Should().BeFalse();
    }

    [Fact]
    public void SameAs_WithNull_IsFalse()
    {
        EventReminders.ExplicitlyNone.SameAs(null).Should().BeFalse();
    }

    [Fact]
    public void Explicit_PreservesValuesTheKioskCouldNotHaveCreated()
    {
        // The read path must never clamp, normalise or de-duplicate. Google itself clamps on the
        // WRITE path (negative -> 0, >40320 -> 40320), but whatever it SENDS must round-trip.
        var odd = EventReminders.Explicit([new("sms", -540), new("popup", 999999)]);

        odd.Overrides.Should().HaveCount(2);
        odd.Overrides[0].Should().Be(new EventReminder("sms", -540));
        odd.Overrides[1].Should().Be(new EventReminder("popup", 999999));
    }

    [Fact]
    public void Explicit_WithNoOverrides_IsExplicitlyNoneShaped()
    {
        var empty = EventReminders.Explicit([]);

        empty.UseDefault.Should().BeFalse();
        empty.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }
}
