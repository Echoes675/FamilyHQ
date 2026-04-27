Feature: Calendar Settings
  As a family member
  I want to manage which calendars are visible and which is the shared family calendar
  So that the dashboard shows the right events for each family member

  Background:
    Given I have a user like "MultiCalUser"
    And I login as the user "MultiCalUser"

  Scenario: Calendars are listed in the settings tab
    When I navigate to the calendar settings tab
    Then I see "Work Calendar" in the calendar settings list
    And I see "Personal Calendar" in the calendar settings list

  Scenario: Hiding a calendar removes its column from the agenda view
    When I hide the "Work Calendar" in calendar settings
    And I view the dashboard
    And I switch to the Agenda View tab
    Then I do not see a column header for "Work Calendar"

  Scenario: Designating a shared calendar marks it as shared
    When I navigate to the calendar settings tab
    And I designate "Family Calendar" as the shared calendar
    Then "Family Calendar" is designated as the shared calendar

  Scenario: Only one calendar can be the shared calendar at a time
    When I navigate to the calendar settings tab
    And I designate "Work Calendar" as the shared calendar
    And I designate "Personal Calendar" as the shared calendar
    Then "Personal Calendar" is designated as the shared calendar
    And "Work Calendar" is no longer designated as the shared calendar

  Scenario: Shared calendar is not selectable as a pill in the event modal
    When I view the dashboard
    And I click the "Agenda" tab
    And I tap the agenda create button
    Then I see the event modal
    And the "Work Calendar" chip is available in the modal
    And the "Personal Calendar" chip is available in the modal
    And the "Family Calendar" chip is not available in the modal

  Scenario: Hidden calendar is not selectable as a pill in the event modal
    When I hide the "Personal Calendar" in calendar settings
    And I view the dashboard
    And I click the "Agenda" tab
    And I tap the agenda create button
    Then I see the event modal
    And the "Work Calendar" chip is available in the modal
    And the "Personal Calendar" chip is not available in the modal

  Scenario: On first login a multi-calendar user automatically has the first calendar designated as shared
    Given I have a user like "AutoDesignateUser"
    And I login as the user "AutoDesignateUser"
    When I navigate to the calendar settings tab
    Then "Primary Calendar" is designated as the shared calendar

  Scenario: Single-calendar accounts have no shared calendar designated
    Given I have a user like "SoloUser"
    And I login as the user "SoloUser"
    When I navigate to the calendar settings tab
    Then "Only Calendar" is no longer designated as the shared calendar

  Scenario: Sync Now button triggers a full calendar sync
    When I navigate to the calendar settings tab
    Then the Sync Now button is visible
    When I click the Sync Now button
    Then I see "Work Calendar" in the calendar settings list

  Scenario: Cancelling the shared calendar confirmation keeps the current shared calendar
    When I navigate to the calendar settings tab
    And I tap the shared toggle for "Work Calendar"
    Then I see the shared calendar confirmation prompt
    When I cancel the shared calendar confirmation
    Then "Family Calendar" is designated as the shared calendar
    And "Work Calendar" is no longer designated as the shared calendar

  Scenario: Shared calendar is automatically hidden and its visibility toggle is disabled
    When I navigate to the calendar settings tab
    Then the visibility toggle for "Family Calendar" is disabled
    And the visibility toggle for "Family Calendar" reads "Hidden"

  Scenario: Register Webhooks button triggers webhook registration
    When I navigate to the calendar settings tab
    Then the Register Webhooks button is visible
    When I click the Register Webhooks button
    Then I see a success message "Webhooks registered."
