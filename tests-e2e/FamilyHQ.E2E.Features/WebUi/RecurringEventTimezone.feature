Feature: Recurring Event Time Zone
  As a family member whose recurring events must stay at the right local time
  I want to set my time zone independently of my location
  So that recurring events are anchored to that zone and survive daylight saving changes

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And I login as the user "TimedEventsUser"
    And I open the Location settings tab

  Scenario: A new user's time zone is auto-detected until one is saved
    Then the timezone is shown as auto-detected
    When I select the timezone "Europe/London"
    And I save the timezone
    Then the timezone is shown as "Europe/London" and saved

  Scenario: Resetting an explicit time zone returns it to auto-detected
    Given I have selected and saved the timezone "Europe/London"
    When I reset the timezone to automatic
    Then the timezone is shown as auto-detected

  Scenario: An explicit time zone is independent of a location reset
    Given I have selected and saved the timezone "Europe/London"
    And I have saved the location "Edinburgh, Scotland"
    When I reset the location to automatic
    Then the timezone is still shown as "Europe/London" and saved

  Scenario: Creating a recurring event sends the configured IANA timezone to Google
    Given I have selected and saved the timezone "Europe/London"
    When I create a weekly recurring event "Standup" in "Appointments"
    Then the event "Standup" was sent to Google with timezone "Europe/London"

  # No explicit timezone: the effective zone follows the SELECTED location. New York
  # (America/New_York) is deliberately different from the auto-detect default (Europe/London),
  # so this proves the zone is derived from the location, not the ip-api fallback. GeoTimeZone
  # on the seeded New York coords is deterministic, so the value is safe to assert.
  Scenario: A selected location's timezone is used when no timezone is set
    Given I have saved the location "New York, USA"
    When I create a weekly recurring event "NY Standup" in "Appointments"
    Then the event "NY Standup" was sent to Google with timezone "America/New_York"

  # With no explicit timezone, switching the custom location re-derives the zone (rule 5b):
  # New York (America/New_York) -> Tokyo (Asia/Tokyo). Both are seeded, deterministic coords.
  Scenario: Changing the selected location re-derives the timezone
    Given I have saved the location "New York, USA"
    And I have saved the location "Tokyo, Japan"
    When I create a weekly recurring event "Tokyo Standup" in "Appointments"
    Then the event "Tokyo Standup" was sent to Google with timezone "Asia/Tokyo"
