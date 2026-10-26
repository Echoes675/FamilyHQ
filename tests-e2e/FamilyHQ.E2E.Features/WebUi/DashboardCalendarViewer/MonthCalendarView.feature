Feature: Month Calendar View
  As a family member
  I want to view my imported events on the dashboard
  So that I can see my upcoming schedule

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"

  Scenario: View upcoming events on the dashboard month view
    When I view the dashboard
    Then I see the event "School Holiday" displayed on the calendar for "tomorrow"

  Scenario: Create a new event
    Given I view the dashboard
    When I create an event "Dentist Appointment"
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Update an existing event
    Given I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I rename the event "School Holiday" to "Doctor Appointment"
    Then I see the event "Doctor Appointment" displayed on the calendar for "tomorrow"

  Scenario: Delete an existing event
    Given I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I delete the event "School Holiday"
    Then I do not see the event "School Holiday" displayed on the calendar for "tomorrow"

  Scenario: View events from all 6 calendars on the month view
    Given I have a user like "SixCalUser"
    And the user has an all-day event "Work Event" tomorrow in "Work Calendar"
    And the user has an all-day event "Personal Event" in 2 days in "Personal Calendar"
    And the user has an all-day event "School Event" in 3 days in "School Calendar"
    And the user has an all-day event "Sports Event" in 4 days in "Sports Calendar"
    And the user has an all-day event "Health Event" in 5 days in "Health Calendar"
    And the user has an all-day event "Hobbies Event" in 6 days in "Hobbies Calendar"
    And I login as the user "SixCalUser"
    When I view the dashboard
    Then I see the event "Work Event" displayed on the calendar for "tomorrow"
    And I see the event "Personal Event" displayed on the calendar for "in 2 days"
    And I see the event "School Event" displayed on the calendar for "in 3 days"
    And I see the event "Sports Event" displayed on the calendar for "in 4 days"
    And I see the event "Health Event" displayed on the calendar for "in 5 days"
    And I see the event "Hobbies Event" displayed on the calendar for "in 6 days"

  Scenario: View events from multiple calendars
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has an all-day event "Family Dinner" tomorrow in "Personal Calendar"
    And the user has an all-day event "Parent Teacher Meeting" tomorrow in "School Calendar"
    And I login as the user "MultiCalendarUser"
    When I view the dashboard
    Then I see the event "Team Meeting" displayed on the calendar for "tomorrow"
    And I see the event "Family Dinner" displayed on the calendar for "tomorrow"
    And I see the event "Parent Teacher Meeting" displayed on the calendar for "tomorrow"

  Scenario: View all-day events
    Given I have a user like "AllDayEventsUser"
    And the "Holidays" calendar is the active calendar
    And the user has an all-day event "Christmas Day" tomorrow
    And the user has an all-day event "New Year's Day" in 7 days
    And I login as the user "AllDayEventsUser"
    When I view the dashboard
    Then I see the event "Christmas Day" displayed on the calendar for "tomorrow"
    And I see the event "New Year's Day" displayed on the calendar for "in 7 days"

  Scenario: View timed events
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a timed event "Dentist Checkup" tomorrow at "10:00" for 1 hour
    And the user has a timed event "Team Standup" tomorrow at "09:00" for 30 minutes
    And I login as the user "TimedEventsUser"
    When I view the dashboard
    Then I see the event "Dentist Checkup" displayed on the calendar for "tomorrow"
    And I see the event "Team Standup" displayed on the calendar for "tomorrow"

  Scenario: View event details
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I click on the event "School Holiday"
    Then I see the event details for "School Holiday"

  Scenario: Update event after changing its calendar
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalendarUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    And I change the event "Team Meeting" to calendar "Personal Calendar"
    When I rename the event "Team Meeting" to "Updated Team Meeting"
    Then I see the event "Updated Team Meeting" displayed on the calendar for "tomorrow"

  Scenario: Delete event after changing its calendar
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalendarUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    And I change the event "Team Meeting" to calendar "Personal Calendar"
    When I delete the event "Team Meeting"
    Then I do not see the event "Team Meeting" displayed on the calendar for "tomorrow"

  Scenario: Navigate to next month
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "Next Month Event" on "next month"
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When I navigate to the next month
    Then I see the event "Next Month Event" displayed on the calendar for "next month"

  Scenario: Create event in two calendars appears twice on grid
    Given I have a user like "MultiCalUser"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I create an event "Standup" in calendars "Work Calendar" and "Personal Calendar"
    Then I see the event "Standup" displayed on the calendar in "Work Calendar" colour
    And I see the event "Standup" displayed on the calendar in "Personal Calendar" colour

  Scenario: Add calendar to existing event via chip
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I open the event "Team Meeting" for editing
    And I add the calendar "Personal Calendar" chip to the event
    Then I see the event "Team Meeting" displayed on the calendar in "Work Calendar" colour
    And I see the event "Team Meeting" displayed on the calendar in "Personal Calendar" colour

  Scenario: Remove calendar chip from event
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I open the event "Team Meeting" for editing
    And I remove the calendar "Personal Calendar" chip from the event
    Then I see the event "Team Meeting" displayed on the calendar in "Work Calendar" colour
    And I do not see a "Personal Calendar" capsule for "Team Meeting" on the calendar

  Scenario: Last chip is protected — cannot remove final calendar
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Solo Event" tomorrow in "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I open the event "Solo Event" for editing
    Then the last active calendar chip has no remove button

  Scenario: Delete event removes it from all calendars
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    And I navigate the month view to show "tomorrow"
    When I delete the event "Team Meeting"
    Then I do not see the event "Team Meeting" displayed on the calendar for "tomorrow"

  Scenario: Month View displays multi-day All-Day events
    Given I have a user like "AllDayEventsUser"
    And the "Holidays" calendar is the active calendar
    And the user has an all-day event "Vacation" spanning 3 days starting tomorrow
    And I login as the user "AllDayEventsUser"
    When I view the dashboard
    Then I see the event "Vacation" displayed on the calendar for "tomorrow"

  Scenario: Month View displays multi-day Timed events
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a timed event "Conference" starting tomorrow at "09:00" spanning 2 days
    And I login as the user "TimedEventsUser"
    When I view the dashboard
    Then I see the event "Conference" displayed on the calendar for "tomorrow"

  # FHQ-32: the create modal must not pre-select any calendar (it used to default to the
  # first calendar in the list — the shared calendar when first — silently routing new
  # events there). Saving with nothing selected must be blocked, not silently assigned.
  Scenario: Create modal requires an explicit calendar and never silently assigns the shared calendar
    Given I have a user like "MultiCalUser"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I open the create-event modal
    Then the create-event modal does not offer the shared "Family Calendar" chip
    When I attempt to save the event "Ghost Event" with no calendar selected
    Then the create-event modal stays open with a calendar validation error
    When I cancel the create-event modal
    Then I do not see the event "Ghost Event" displayed on the calendar
    When I create an event "Team Lunch" in calendar "Work Calendar"
    Then I see the event "Team Lunch" displayed on the calendar in "Work Calendar" colour
    And I do not see a "Family Calendar" capsule for "Team Lunch" on the calendar

  # Every dated assertion above is only as good as its claim that the grid gets moved to the date
  # first, and on most days that claim is untested: the seeded date is already on screen, so the
  # navigation is a no-op and an assertion that could not survive without it still passes. The day it
  # IS needed arrives on the calendar's terms — the grid is a window of whole weeks, so it stops
  # dead on a month that ends on a Saturday and the next month's grid starts with no room for the
  # one before it. That pairing comes round about one month in seven, and in clumps — far too rare to
  # be the only thing standing between this feature and a day of red.
  #
  # So the view is displaced deliberately instead of waited for. The kiosk's own clock bridge rolls
  # the displayed date forward a month and the idle check snaps the grid to it, which leaves the
  # seeded event a month behind the view — the same situation a boundary produces, reachable on any
  # run date. What it does NOT reproduce is the boundary arithmetic itself: it proves the assertion
  # brings the view to its date, not that any particular adjacency of grids is handled. The
  # arithmetic has no test that does not wait for the date.
  #
  # Needs the clock override, so it is tagged like the other scenarios that drive that bridge and,
  # like them, it cannot run where the flag is off.
  @day-rollover
  Scenario: An event is still asserted on its own day after the kiosk's date has moved on
    When I view the dashboard
    And the kiosk's idle timer reads 16 minutes
    And the date rolls over by 1 month
    And the idle check runs
    And I switch to the Month View tab
    Then I see the event "School Holiday" displayed on the calendar for "tomorrow"
