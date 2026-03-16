namespace FamilyHQ.Simulator.Data;

using FamilyHQ.Simulator.Models;

public class DataSeeder
{
    public static void SeedData(SimContext db)
    {
        if (db.Users.Any())
            return;
        
        var defaultUserId = "default_simulator_user" + Guid.NewGuid().ToString("N");
        db.Users.Add(new SimulatedUser { Id = defaultUserId, Username = defaultUserId });

        var familyCalendarId = "simulated_calendar_family" + Guid.NewGuid().ToString("N");
        var workCalendarId = "simulated_calendar_work" + Guid.NewGuid().ToString("N");
        db.Calendars.AddRange(
            new SimulatedCalendar { Id = familyCalendarId, Summary = "Family Calendar", BackgroundColor = "#b39ddb", UserId = defaultUserId },
            new SimulatedCalendar { Id = "simulated_calendar_work", Summary = "Work Calendar", BackgroundColor = "#9e9e9e", UserId = defaultUserId }
        );

        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        db.Events.AddRange(
            new SimulatedEvent
            {
                Id = "evt_seed_1",
                CalendarId = familyCalendarId,
                Summary = "Dentist Appointment",
                Location = "123 Main St",
                StartTime = startOfMonth.AddDays(10).AddHours(14),
                EndTime = startOfMonth.AddDays(10).AddHours(15),
                IsAllDay = false,
                UserId = defaultUserId
            },
            new SimulatedEvent
            {
                Id = "evt_seed_2",
                CalendarId = familyCalendarId,
                Summary = "Family Dinner",
                Location = "Home",
                StartTime = startOfMonth.AddDays(15).AddHours(18),
                EndTime = startOfMonth.AddDays(15).AddHours(20),
                IsAllDay = false,
                UserId = defaultUserId
            },
            new SimulatedEvent
            {
                Id = "evt_seed_3",
                CalendarId = workCalendarId,
                Summary = "Project Sync",
                Location = "Microsoft Teams",
                StartTime = startOfMonth.AddDays(12).AddHours(9),
                EndTime = startOfMonth.AddDays(12).AddHours(10),
                IsAllDay = false,
                UserId = defaultUserId
            },
            new SimulatedEvent
            {
                Id = "evt_seed_4",
                CalendarId = workCalendarId,
                Summary = "Quarterly Review",
                Location = "Conference Room A",
                StartTime = startOfMonth.AddDays(20).AddHours(13),
                EndTime = startOfMonth.AddDays(20).AddHours(15),
                IsAllDay = false,
                UserId = defaultUserId
            },
            new SimulatedEvent
            {
                Id = "evt_seed_5",
                CalendarId = familyCalendarId,
                Summary = "School Holiday",
                StartTime = startOfMonth.AddDays(5),
                EndTime = startOfMonth.AddDays(6),
                IsAllDay = true,
                UserId = defaultUserId
            }
        );

        db.SaveChanges();
        Console.WriteLine("[SIM] Data seeded.");
    }
}
