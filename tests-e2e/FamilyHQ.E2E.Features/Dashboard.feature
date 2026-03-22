Feature: Dashboard Calendar Viewer
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
    Then I see the event "School Holiday" displayed on the calendar

  Scenario: Create a new event
    Given I view the dashboard
    When I create an event "Dentist Appointment"
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Update an existing event
    Given I view the dashboard
    When I rename the event "School Holiday" to "Doctor Appointment"
    Then I see the event "Doctor Appointment" displayed on the calendar

  Scenario: Delete an existing event
    Given I view the dashboard
    When I delete the event "School Holiday"
    Then I do not see the event "School Holiday" displayed on the calendar

  Scenario: View events from multiple calendars
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has an all-day event "Family Dinner" tomorrow in "Personal Calendar"
    And the user has an all-day event "Parent Teacher Meeting" tomorrow in "School Calendar"
    And I login as the user "MultiCalendarUser"
    When I view the dashboard
    Then I see the event "Team Meeting" displayed on the calendar
    And I see the event "Family Dinner" displayed on the calendar
    And I see the event "Parent Teacher Meeting" displayed on the calendar

  Scenario: View all-day events
    Given I have a user like "AllDayEventsUser"
    And the "Holidays" calendar is the active calendar
    And the user has an all-day event "Christmas Day" tomorrow
    And the user has an all-day event "New Year's Day" in 7 days
    And I login as the user "AllDayEventsUser"
    When I view the dashboard
    Then I see the event "Christmas Day" displayed on the calendar
    And I see the event "New Year's Day" displayed on the calendar

  Scenario: View timed events
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a timed event "Dentist Checkup" tomorrow at "10:00" for 1 hour
    And the user has a timed event "Team Standup" tomorrow at "09:00" for 30 minutes
    And I login as the user "TimedEventsUser"
    When I view the dashboard
    Then I see the event "Dentist Checkup" displayed on the calendar
    And I see the event "Team Standup" displayed on the calendar

  Scenario: View event details
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When I click on the event "School Holiday"
    Then I see the event details for "School Holiday"

  Scenario: Update event after changing its calendar
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalendarUser"
    And I view the dashboard
    And I change the event "Team Meeting" to calendar "Personal Calendar"
    When I rename the event "Team Meeting" to "Updated Team Meeting"
    Then I see the event "Updated Team Meeting" displayed on the calendar

  Scenario: Delete event after changing its calendar
    Given I have a user like "MultiCalendarUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And I login as the user "MultiCalendarUser"
    And I view the dashboard
    And I change the event "Team Meeting" to calendar "Personal Calendar"
    When I delete the event "Team Meeting"
    Then I do not see the event "Team Meeting" displayed on the calendar

  Scenario: Navigate to next month
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "Next Month Event" in 30 days
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When I navigate to the next month
    Then I see the event "Next Month Event" displayed on the calendar

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
    When I open the event "Team Meeting" for editing
    And I remove the calendar "Personal Calendar" chip from the event
    Then I see the event "Team Meeting" displayed on the calendar in "Work Calendar" colour
    And I do not see a "Personal Calendar" capsule for "Team Meeting" on the calendar

  Scenario: Last chip is protected — cannot remove final calendar
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Solo Event" tomorrow in "Work Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I open the event "Solo Event" for editing
    Then the last active calendar chip has no remove button

  Scenario: Delete event removes it from all calendars
    Given I have a user like "MultiCalUser"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    And I view the dashboard
    When I delete the event "Team Meeting"
    Then I do not see the event "Team Meeting" displayed on the calendar
