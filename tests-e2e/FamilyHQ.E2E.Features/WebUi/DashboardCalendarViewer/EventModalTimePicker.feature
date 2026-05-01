Feature: Event Modal Time Picker
  As a family member creating or editing an event
  I want adjusting the start time to keep the same event duration
  And adjusting the end time to leave the start time alone
  So that I can quickly reschedule an event without re-entering both ends

  Background:
    Given I have a user like "TimePickerUser"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TimePickerUser"

  Scenario: Adjusting the start time shifts the end time by the same delta
    When I view the dashboard
    And I switch to the Day View tab
    And I click an empty grid slot for calendar "Family Events" at "14:00"
    Then the start time picker shows "14:00"
    And the end time picker shows "15:00"
    When I press the "Increase hour" button on the start time picker 1 time
    Then the start time picker shows "15:00"
    And the end time picker shows "16:00"
    When I press the "Increase minute" button on the start time picker 3 times
    Then the start time picker shows "15:30"
    And the end time picker shows "16:30"
    When I press the "Decrease hour" button on the start time picker 2 times
    Then the start time picker shows "13:30"
    And the end time picker shows "14:30"

  Scenario: Adjusting the end time leaves the start time unchanged
    When I view the dashboard
    And I switch to the Day View tab
    And I click an empty grid slot for calendar "Family Events" at "14:00"
    Then the start time picker shows "14:00"
    And the end time picker shows "15:00"
    When I press the "Decrease minute" button on the end time picker 4 times
    Then the start time picker shows "14:00"
    And the end time picker shows "14:20"
    When I press the "Increase hour" button on the end time picker 2 times
    Then the start time picker shows "14:00"
    And the end time picker shows "16:20"
