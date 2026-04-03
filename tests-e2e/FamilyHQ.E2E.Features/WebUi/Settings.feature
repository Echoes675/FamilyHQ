Feature: Settings Page
  As an authenticated user
  I want to manage my settings
  So that I can configure my location, view my theme schedule, and sign out

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page

  Scenario: Back button returns user to the dashboard
    When I click the back button
    Then I see the calendar displayed

  Scenario: Location tab shows no saved location hint
    When I navigate to the location tab
    Then I see the no saved location hint

  Scenario: User can save a location
    When I navigate to the location tab
    And I enter "Edinburgh, Scotland" as the place name
    And I click save location
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Saved" badge on the location pill

  Scenario: Theme tiles are visible on the display tab
    When I navigate to the display tab
    Then I see the Morning theme tile with a time
    And I see the Daytime theme tile with a time
    And I see the Evening theme tile with a time
    And I see the Night theme tile with a time

  Scenario: General tab shows the signed-in username
    Then I see the username in the account section

  Scenario: User can sign out from the settings page
    When I click the sign out button on the settings page
    Then I see the "Login to Google" button
