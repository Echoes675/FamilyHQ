Feature: Weather override dev tool
  As a developer working in dev or staging
  I want to manually force weather animations
  So that I can visually verify each condition's animation without seeding real weather data

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"

  @ignore
  Scenario: Activating override and selecting a condition engages the matching animation
    When I navigate to the settings page
    And I navigate to the Weather Override tab
    And I toggle override on
    And I select the "HeavyRain" condition
    Then the weather overlay element has the class "weather-heavyrain"

  @ignore
  Scenario: Toggling Windy adds the windy class
    When I navigate to the settings page
    And I navigate to the Weather Override tab
    And I toggle override on
    And I select the "Snow" condition
    And I toggle Windy on
    Then the weather overlay element has the class "weather-snow"
    And the weather overlay element has the class "weather-windy"

  @ignore
  Scenario: Deactivating override returns the overlay to real weather behaviour
    Given weather is enabled
    And the user has a saved location "TestCity" at 55.95, -3.19
    And weather data is seeded for the location:
      | Current Temp | Current Code | Wind Speed |
      | 12           | 0            | 5          |
    When I wait for weather data to load
    And I navigate to the settings page
    And I navigate to the Weather Override tab
    And I toggle override on
    And I select the "HeavyRain" condition
    Then the weather overlay element has the class "weather-heavyrain"
    When I toggle override off
    Then the weather overlay element does not have the class "weather-heavyrain"
