Feature: Dashboard Calendar Viewer
  As a family member
  I want to view my imported events on the dashboard
  So that I can see my upcoming schedule

  Background:
    Given I have a user like "Test Family Member" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "Test Family Member"

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
    Given I have a user like "Multi Calendar User" with calendar "Work Calendar"
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has an all-day event "Family Dinner" tomorrow in "Personal Calendar"
    And the user has an all-day event "Parent Teacher Meeting" tomorrow in "School Calendar"
    And I login as the user "Multi Calendar User"
    When I view the dashboard
    Then I see the event "Team Meeting" displayed on the calendar
    And I see the event "Family Dinner" displayed on the calendar
    And I see the event "Parent Teacher Meeting" displayed on the calendar

  Scenario: View all-day events
    Given I have a user like "All Day Events User" with calendar "Holidays"
    And the user has an all-day event "Christmas Day" tomorrow
    And the user has an all-day event "New Year's Day" in 7 days
    And I login as the user "All Day Events User"
    When I view the dashboard
    Then I see the event "Christmas Day" displayed on the calendar
    And I see the event "New Year's Day" displayed on the calendar

  Scenario: View timed events
    Given I have a user like "Timed Events User" with calendar "Appointments"
    And the user has a timed event "Dentist Checkup" tomorrow at "10:00" for 1 hour
    And the user has a timed event "Team Standup" tomorrow at "09:00" for 30 minutes
    And I login as the user "Timed Events User"
    When I view the dashboard
    Then I see the event "Dentist Checkup" displayed on the calendar
    And I see the event "Team Standup" displayed on the calendar

  Scenario: View event details
    Given I have a user like "Test Family Member" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "Test Family Member"
    And I view the dashboard
    When I click on the event "School Holiday"
    Then I see the event details for "School Holiday"

  Scenario: Navigate to next month
    Given I have a user like "Test Family Member" with calendar "Family Events"
    And the user has an all-day event "Next Month Event" in 30 days
    And I login as the user "Test Family Member"
    And I view the dashboard
    When I navigate to the next month
    Then I see the event "Next Month Event" displayed on the calendar
