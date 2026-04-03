Feature: Weather Settings
  As an authenticated user
  I want to configure my weather preferences
  So that weather data displays according to my needs

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"

  Scenario: Weather settings are accessible from the settings page
    Given I am on the settings page
    When I navigate to the weather tab
    Then I see the weather enabled toggle

  Scenario: Weather settings tab shows all form fields
    When I navigate to weather settings
    Then I see the weather enabled toggle
    And I see the temperature unit selector
    And I see the poll interval input
    And I see the wind threshold input

  Scenario: Save and cancel buttons appear only after changes
    When I navigate to weather settings
    Then the save button is not visible
    When I change the temperature unit
    Then the save button is visible
    And the cancel button is visible

  Scenario: Cancel reverts unsaved changes
    When I navigate to weather settings
    And I change the temperature unit
    And I click cancel on weather settings
    Then the temperature unit shows the original value

  Scenario: Saving settings shows success message
    When I navigate to weather settings
    And I change the poll interval to 2
    And I save weather settings
    Then I see the "Settings saved." confirmation

  Scenario: Back button returns to dashboard
    When I navigate to weather settings
    And I click the back button
    Then I see the calendar displayed
