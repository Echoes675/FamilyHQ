Feature: Kiosk Dashboard Views
  As a family member
  I want to view my calendar in different formats
  So that I can see my schedule at a glance

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"

  Scenario: Today View loads as default
    When I view the dashboard
    Then I see the Day View Container
    And the current time line is visible

  Scenario: Switch to Month View
    Given I view the dashboard
    When I switch to the Month View tab
    Then I see the Month View Table

  Scenario: Switch to Week View
    Given I view the dashboard
    When I switch to the Day View tab
    Then I see the Day View Container

  Scenario: Navigate to next month
    Given I view the dashboard
    And I switch to the Month View tab
    When I navigate to the next month
    Then I see the Month View Table

  Scenario: Open event modal from Day View
    Given I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    And I have a user like "EventModalUser"
    And the "Family Events" calendar is the active calendar
    And the user has a timed event "Team Meeting" tomorrow at "14:00" for 60 minutes
    And I login as the user "EventModalUser"
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    When I click on the event "Team Meeting"
    Then I see the event details for "Team Meeting"

  Scenario: Create new event via FAB button
    Given I view the dashboard
    When I click an empty grid slot for calendar "Family Events" at "14:00"
    Then I see the event modal
