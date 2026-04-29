using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// Note: when the user types invalid text, the TimePicker component intentionally
// keeps the typed text visible (flagged with .is-invalid) rather than reverting,
// so the user can fix their typo. Component-level reversion is not asserted here.
public class TimePickerLogicTests
{
    [Fact]
    public void IncrementHour_AtTwentyThree_WrapsToZero()
    {
        var result = TimePickerLogic.IncrementHour(new TimeOnly(23, 30));
        result.Should().Be(new TimeOnly(0, 30));
    }

    [Fact]
    public void DecrementHour_AtZero_WrapsToTwentyThree()
    {
        var result = TimePickerLogic.DecrementHour(new TimeOnly(0, 15));
        result.Should().Be(new TimeOnly(23, 15));
    }

    [Fact]
    public void IncrementMinute_FiftyFive_CarriesIntoHour()
    {
        var result = TimePickerLogic.IncrementMinute(new TimeOnly(10, 55));
        result.Should().Be(new TimeOnly(11, 0));
    }

    [Fact]
    public void IncrementMinute_EndOfDay_WrapsToZero()
    {
        var result = TimePickerLogic.IncrementMinute(new TimeOnly(23, 55));
        result.Should().Be(new TimeOnly(0, 0));
    }

    [Fact]
    public void DecrementMinute_AtTopOfHour_GoesToFiftyFiveOfPreviousHour()
    {
        var result = TimePickerLogic.DecrementMinute(new TimeOnly(11, 0));
        result.Should().Be(new TimeOnly(10, 55));
    }

    [Fact]
    public void DecrementMinute_AtMidnight_WrapsToTwentyThreeFiftyFive()
    {
        var result = TimePickerLogic.DecrementMinute(new TimeOnly(0, 0));
        result.Should().Be(new TimeOnly(23, 55));
    }

    [Fact]
    public void TryParse_ValidHHmm_ReturnsParsedTime()
    {
        var ok = TimePickerLogic.TryParse("09:30", out var result);
        ok.Should().BeTrue();
        result.Should().Be(new TimeOnly(9, 30));
    }

    [Fact]
    public void TryParse_SingleDigitMinutes_IsRejected()
    {
        // Decision: require strict HH:mm (zero-padded) for predictability on a kiosk
        // touchscreen — ambiguous "9:5" would otherwise resolve to 09:05 or 09:50
        // depending on timing of subsequent input. Reject and let the user retype.
        var ok = TimePickerLogic.TryParse("9:5", out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParse_NonNumeric_ReturnsFalse()
    {
        var ok = TimePickerLogic.TryParse("abc", out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void TryParse_OutOfRangeHour_ReturnsFalse()
    {
        var ok = TimePickerLogic.TryParse("25:00", out _);
        ok.Should().BeFalse();
    }
}
