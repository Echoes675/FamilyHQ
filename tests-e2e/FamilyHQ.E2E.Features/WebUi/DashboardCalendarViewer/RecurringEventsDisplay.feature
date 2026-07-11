Feature: Recurring Event Display
  As a family member
  I want recurring Google Calendar events to appear as individual occurrences
  So that my dashboard reflects every instance of a repeating commitment

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a weekly recurring event "Soccer practice" for 3 occurrences in "Appointments"
    And I login as the user "TimedEventsUser"

  Scenario: A weekly series syncs into exactly one occurrence per week at a uniform time
    When Google Calendar sends a webhook notification
    And I view the dashboard
    And I switch to the Day View tab
    Then the series "Soccer practice" occupies exactly its 3 weekly occurrence slots

  Scenario: A synced recurring instance is marked with a recurrence indicator
    When Google Calendar sends a webhook notification
    And I view the dashboard
    Then the recurring event shows a recurrence indicator

  Scenario: Opening a recurring instance reveals its repeat pattern
    When Google Calendar sends a webhook notification
    And I view the dashboard
    And I click on the event "Soccer practice"
    Then the recurring event details describe the weekly repeat pattern

  Scenario: The recurrence indicator appears in the Day view
    When Google Calendar sends a webhook notification
    And I view the dashboard
    And I switch to the Day View tab
    And I select the recurring event's first occurrence in the day picker
    Then the recurring event shows a recurrence indicator

  Scenario: The recurrence indicator appears in the Agenda view
    When Google Calendar sends a webhook notification
    And I view the dashboard
    And I switch to the Agenda View tab
    Then the recurring event shows a recurrence indicator
