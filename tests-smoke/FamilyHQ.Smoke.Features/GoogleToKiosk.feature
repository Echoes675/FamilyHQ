@kiosk
Feature: What Google pushes to the kiosk

  These scenarios make the change in Google exactly as a phone would, then wait for the live push to
  travel Google → RelayRobin → preprod's webhook → the sync queue → the API. Nothing here triggers a
  sync by hand: a scenario that did would pass on an environment whose push path is completely dead,
  which is the outage this suite exists to catch.

  Background:
    Given the preprod kiosk is open and signed in

  @GK2
  Scenario: A shared-calendar event naming two members appears for both of them
    When an event naming two members is created in Google's shared calendar
    Then preprod serves that event for both named members and no others
    And the kiosk shows that event

  @GK1
  Scenario: An event created in Google on one member's calendar belongs to that member alone
    Given an event is created in Google on a member's calendar
    And the kiosk has received that event
    Then preprod serves that event for that member and no others

  @GK3
  Scenario: Renaming the members in a Google description changes who the event belongs to
    Given an event naming two members is created in Google's shared calendar
    And the kiosk has received that event
    When the description is changed in Google to name a different pair of members
    Then preprod serves that event for the newly named members and no others

  @GK4
  Scenario: An event deleted in Google disappears from the kiosk
    Given an event is created in Google on a member's calendar
    And the kiosk has received that event
    When that event is deleted in Google
    Then preprod stops serving that event
