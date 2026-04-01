Feature: Authentication
  As a user of the FamilyHQ dashboard
  I want to be able to sign in and sign out
  So that I can access my calendar securely

  Scenario: User sees sign-in button when not authenticated
    Given I am not authenticated
    When I navigate to the dashboard
    Then I see the "Login to Google" button

  Scenario: User can sign in and see their username
    Given I have a user like "TestFamilyMember"
    And I am not authenticated
    When I sign in as the user "TestFamilyMember"
    Then I see the username displayed
    And I see the "Sign Out" button

  Scenario: User can sign out and return to sign-in screen
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    When I click the "Sign Out" button
    Then I see the "Login to Google" button
    And I do not see the username displayed

  Scenario: Calendar is hidden when not authenticated
    Given I am not authenticated
    When I navigate to the dashboard
    Then I do not see the calendar

  Scenario: Calendar is visible when authenticated
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I am signed in as the user "TestFamilyMember"
    When I view the dashboard
    Then I see the calendar displayed
    And I see the event "School Holiday" displayed on the calendar

  Scenario: Settings page requires authentication
    Given I am not authenticated
    When I navigate to the settings page
    Then I see the "Login to Google" button
