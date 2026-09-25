@kiosk
Feature: Open-Meteo reaches the preprod kiosk

  Open-Meteo is the one third party in this suite that is not Google. The strip renders only when
  preprod actually holds a current reading, and it only holds one because it asked Open-Meteo for the
  coordinates behind its saved place name — so a populated strip is the whole chain working.

  Background:
    Given the preprod kiosk is open and signed in

  @E1
  Scenario: The dashboard shows live current conditions for the saved location
    Then the weather widget shows current conditions
    And the kiosk reported no console errors
