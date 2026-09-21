namespace FamilyHQ.Simulator.Data;

using System.Text.Json;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;

public class DataSeeder
{
    public static void SeedData(SimContext db, ILogger logger)
    {
        if (!db.Users.Any())
        {
            var defaultUserId = "default_simulator_user" + Guid.NewGuid().ToString("N");
            db.Users.Add(new SimulatedUser { Id = defaultUserId, Username = defaultUserId });

            var familyCalendarId = "simulated_calendar_family" + Guid.NewGuid().ToString("N");
            var workCalendarId = "simulated_calendar_work" + Guid.NewGuid().ToString("N");
            db.Calendars.AddRange(
                new SimulatedCalendar
                {
                    Id = familyCalendarId, Summary = "Family Calendar", BackgroundColor = "#b39ddb", UserId = defaultUserId,
                    // FHQ-189 (I3): exercises GetCalendarsAsync's defaultReminders mapping in CI —
                    // the Simulator emitted no reminders at all before this fix.
                    DefaultRemindersJson = JsonSerializer.Serialize(
                        new List<GoogleEventReminderOverride> { new("popup", 30) })
                },
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
                    UserId = defaultUserId,
                    // FHQ-189 (I3): state 1 of 4 — inherits the calendar's defaults.
                    RemindersJson = JsonSerializer.Serialize(new GoogleEventReminders(UseDefault: true, Overrides: null))
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
                    UserId = defaultUserId,
                    // FHQ-189 (I3): state 2 of 4 — an explicit override list of its own.
                    RemindersJson = JsonSerializer.Serialize(new GoogleEventReminders(
                        UseDefault: false,
                        Overrides: [new("popup", 10), new("email", 60)]))
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
                    UserId = defaultUserId,
                    // FHQ-189 (I3): state 3 of 4 — explicitly none (useDefault:false, no overrides key).
                    RemindersJson = JsonSerializer.Serialize(new GoogleEventReminders(UseDefault: false, Overrides: null))
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
                    // FHQ-189 (I3): deliberately left with no reminders at all (RemindersJson null) —
                    // "not yet synced", the fourth shape the read path must tell apart from the three above.
                },
                new SimulatedEvent
                {
                    Id = "evt_seed_5",
                    CalendarId = familyCalendarId,
                    Summary = "School Holiday",
                    StartTime = startOfMonth.AddDays(5),
                    EndTime = startOfMonth.AddDays(6),
                    IsAllDay = true,
                    UserId = defaultUserId,
                    // FHQ-189 (I3): state 4 of 4 — an all-day event carrying the calendar default
                    // MATERIALISED into an explicit override (Google never lets an all-day event
                    // inherit — see EventReminders remarks), matching the family calendar's own
                    // DefaultRemindersJson above.
                    RemindersJson = JsonSerializer.Serialize(new GoogleEventReminders(
                        UseDefault: false,
                        Overrides: [new("popup", 30)]))
                }
            );

            db.SaveChanges();
            logger.LogInformation("[SIM] Data seeded.");
        }

        if (!db.SimulatedLocations.Any())
        {
            db.SimulatedLocations.AddRange(
                new SimulatedLocation { PlaceName = "Edinburgh, Scotland", Latitude = 55.9533, Longitude = -3.1883 },
                new SimulatedLocation { PlaceName = "London, England", Latitude = 51.5074, Longitude = -0.1278 },
                new SimulatedLocation { PlaceName = "Dublin, Ireland", Latitude = 53.3498, Longitude = -6.2603 },
                new SimulatedLocation { PlaceName = "New York, USA", Latitude = 40.7128, Longitude = -74.0060 },
                new SimulatedLocation { PlaceName = "Tokyo, Japan", Latitude = 35.6762, Longitude = 139.6503 },
                new SimulatedLocation { PlaceName = "Sydney, Australia", Latitude = -33.8688, Longitude = 151.2093 }
            );
            db.SaveChanges();
        }
    }
}
