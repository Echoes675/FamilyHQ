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

  @GK4
  Scenario: An event deleted in Google disappears from the kiosk
    Given an event is created in Google on a member's calendar
    And the kiosk has received that event
    When that event is deleted in Google
    Then preprod stops serving that event
