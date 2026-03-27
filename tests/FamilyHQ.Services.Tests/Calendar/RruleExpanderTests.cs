using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class RruleExpanderTests
{
    private readonly RruleExpander _sut = new();

    private static CalendarEvent CreateMasterEvent(string rrule, DateTimeOffset start, DateTimeOffset end)
    {
        return new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Title = "Test Event",
            Start = start,
            End = end,
            RecurrenceRule = rrule,
            IsRecurrenceException = false,
            GoogleEventId = "test-google-id"
        };
    }

    [Fact]
    public void ExpandRecurringEvent_DailyRule_ReturnsCorrectInstances()
    {
        var start = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var master = CreateMasterEvent("FREQ=DAILY;COUNT=5", start, end);
        
        var rangeStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero);
        
        var instances = _sut.ExpandRecurringEvent(master, rangeStart, rangeEnd).ToList();
        
        Assert.Equal(5, instances.Count);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero), instances[0].Start);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 9, 0, 0, TimeSpan.Zero), instances[4].Start);
    }

    [Fact]
    public void ExpandRecurringEvent_WeeklyRule_ReturnsWeeklyInstances()
    {
        var start = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero); // Monday
        var end = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var master = CreateMasterEvent("FREQ=WEEKLY;COUNT=4", start, end);
        
        var rangeStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
        
        var instances = _sut.ExpandRecurringEvent(master, rangeStart, rangeEnd).ToList();
        
        Assert.Equal(4, instances.Count);
        // Each instance should be 7 days apart
        for (int i = 1; i < instances.Count; i++)
        {
            Assert.Equal(7, (instances[i].Start - instances[i - 1].Start).Days);
        }
    }

    [Fact]
    public void ExpandRecurringEvent_NoRrule_ReturnsEmpty()
    {
        var start = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var master = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            Title = "Non-recurring",
            Start = start,
            End = end,
            RecurrenceRule = null,
            GoogleEventId = "test-google-id"
        };
        
        var instances = _sut.ExpandRecurringEvent(master, start, end.AddDays(30)).ToList();
        
        Assert.Empty(instances);
    }

    [Fact]
    public void ExpandRecurringEvent_RangeFilter_OnlyReturnsInstancesInRange()
    {
        var start = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var master = CreateMasterEvent("FREQ=DAILY", start, end); // No COUNT — infinite
        
        var rangeStart = new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero);
        
        var instances = _sut.ExpandRecurringEvent(master, rangeStart, rangeEnd).ToList();
        
        // Should only return instances within the range (Jan 5-9)
        Assert.All(instances, i => Assert.True(i.Start >= rangeStart && i.Start < rangeEnd));
    }

    [Fact]
    public void ExpandRecurringEvent_PreservesEventDuration()
    {
        var start = new DateTimeOffset(2024, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2024, 1, 1, 11, 30, 0, TimeSpan.Zero); // 2.5 hour event
        var master = CreateMasterEvent("FREQ=DAILY;COUNT=3", start, end);
        
        var instances = _sut.ExpandRecurringEvent(master, start, end.AddDays(10)).ToList();
        
        Assert.All(instances, i => Assert.Equal(TimeSpan.FromHours(2.5), i.End - i.Start));
    }
}
