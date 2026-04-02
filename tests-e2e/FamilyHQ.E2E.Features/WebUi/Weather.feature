Feature: Weather Integration
  As an authenticated user with a saved location
  I want to see weather data on my dashboard
  So that I can plan my day around the weather

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And weather is enabled
    And the user has a saved location "TestCity" at 55.95, -3.19
    And weather data is seeded for the location:
      | Current Temp | Current Code | Wind Speed |
      | 12           | 61           | 15         |
    And daily forecast data is seeded for the location:
      | Date      | Code | High | Low | WindMax |
      | today     | 61   | 14   | 8   | 20      |
      | tomorrow  | 3    | 16   | 9   | 10      |
      | in 2 days | 0    | 18   | 11  | 5       |

  Scenario: Weather strip shows current temperature and condition
    When I wait for weather data to load
    Then the weather strip is visible
    And the weather strip shows a temperature
    And the weather strip shows condition "Light Rain"

  Scenario: Weather strip shows forecast days
    When I wait for weather data to load
    Then I see forecast days in the weather strip

  Scenario: Weather overlay shows correct condition class
    When I wait for weather data to load
    Then the weather overlay has class "weather-lightrain"

  Scenario: Weather overlay shows windy modifier when wind exceeds threshold
    Given weather data is seeded for the location:
      | Current Temp | Current Code | Wind Speed |
      | 12           | 61           | 50         |
    When I wait for weather data to load
    Then the weather overlay has class "weather-lightrain"
    And the weather overlay has class "weather-windy"

  Scenario: Agenda view shows temperatures per day
    When I switch to the Agenda View tab
    And I wait for weather data to load
    Then the agenda row for "today" shows weather temperatures
    And the agenda row for "tomorrow" shows weather temperatures

  Scenario: Day view shows hourly temperatures
    Given hourly weather data is seeded for "today":
      | Hour  | Code | Temp | Wind |
      | 08:00 | 3    | 10   | 12   |
      | 09:00 | 3    | 11   | 11   |
      | 10:00 | 61   | 12   | 15   |
    When I switch to the Day View tab
    And I wait for weather data to load
    Then I see hourly temperatures in the day view

  Scenario: Disabling weather hides the weather strip
    When I wait for weather data to load
    Then the weather strip is visible
    When I navigate to weather settings
    And I disable weather
    And I save weather settings
    And I click the back button
    And I click the back button
    Then the weather strip is not visible

  Scenario: Disabling weather clears the overlay
    When I wait for weather data to load
    Then the weather overlay has class "weather-lightrain"
    When I navigate to weather settings
    And I disable weather
    And I save weather settings
    And I click the back button
    And I click the back button
    Then the weather overlay has no condition class
