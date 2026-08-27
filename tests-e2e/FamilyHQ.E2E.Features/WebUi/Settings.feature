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

  Scenario: Calendars tab is the second tab on the settings page
    Then the settings tab in position 2 is "Calendars"

  Scenario: Location tab shows auto-detected location when none saved
    When I navigate to the location tab
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Auto" badge on the location pill

  Scenario: User can save a location
    When I navigate to the location tab
    And I enter "Edinburgh, Scotland" as the place name
    And I click save location
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Saved" badge on the location pill

  # FHQ-177: the theme is derived from the kiosk's SAVED location, so one has to exist before there
  # are any times to show. This used to pass without saving anything because the boundaries came from
  # an IP lookup — which in production returns the hosting datacentre, not the family's home.
  Scenario: Theme tiles are visible on the display tab
    When I navigate to the location tab
    And I enter "Edinburgh, Scotland" as the place name
    And I click save location
    # Barrier, not decoration: clicking save does not await the POST, and the display tab reads the
    # boundaries once when it is opened, so the tab can initialise before the row exists (Deploy-Dev
    # #708). It must be the BADGE: the pill shows the auto-detected city pre-save, and dev's simulated
    # geolocation is Edinburgh too, so asserting the pill text matched a value that was already there
    # and returned in 0.0s — no barrier at all (Deploy-Dev #712). The badge only flips Auto -> Saved
    # on the server response, by which point SaveLocation has awaited the recalculation.
    Then I see the "Saved" badge on the location pill
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

  Scenario: Theme tiles are not selectable when auto-change is enabled
    When I navigate to the display tab
    Then the theme tiles are not selectable

  Scenario: Selecting a theme tile applies it when auto-change is disabled
    When I navigate to the display tab
    And I disable auto-change theme
    And I select the "Night" theme tile
    Then the "Night" theme tile is selected

  Scenario: Diagnostics data is not loaded on the default settings view
    Then I do not see the diagnostics connection status

  Scenario: Opening the diagnostics tab shows the connection status
    When I navigate to the diagnostics tab
    Then I see the diagnostics connection status

  Scenario: General tab no longer links to a standalone diagnostics page
    Then I do not see the diagnostics link on the general tab

  Scenario: Sync All on the diagnostics tab triggers a full calendar sync
    When I navigate to the diagnostics tab
    And I click the Sync All button
    Then I see the diagnostics sync completed message
