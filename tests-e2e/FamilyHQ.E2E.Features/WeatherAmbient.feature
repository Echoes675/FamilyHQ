Feature: Weather and Ambient Background
  As a kiosk user
  I want the background to reflect the current weather and time of day
  So that the display feels alive and contextual

  Background:
    Given I have a user like "TestFamilyMember"
    And I login as the user "TestFamilyMember"

  Scenario: Ambient background shows correct circadian state
    Given I view the dashboard
    Then the ambient background should have a circadian gradient class

  Scenario: Weather overlay changes when simulator sets heavy rain
    Given I view the dashboard
    When the simulator sets weather to "HeavyRain"
    And I wait for the weather to update
    Then the ambient background should have the "weather-heavy-rain" CSS class

  Scenario: Weather overlay shows clear when simulator sets clear
    Given I view the dashboard
    When the simulator sets weather to "Clear"
    And I wait for the weather to update
    Then the ambient background should have the "weather-clear" CSS class

  Scenario: Weather overlay shows cloudy when simulator sets cloudy
    Given I view the dashboard
    When the simulator sets weather to "Cloudy"
    And I wait for the weather to update
    Then the ambient background should have the "weather-cloudy" CSS class

  Scenario: Weather overlay shows light rain when simulator sets light rain
    Given I view the dashboard
    When the simulator sets weather to "LightRain"
    And I wait for the weather to update
    Then the ambient background should have the "weather-light-rain" CSS class
