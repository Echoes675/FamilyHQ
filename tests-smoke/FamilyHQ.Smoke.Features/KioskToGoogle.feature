@kiosk
Feature: What the kiosk writes to Google

  The kiosk is a full read-write client: it creates, edits and deletes events just as the phone app
  does. Every assertion here reads from Google rather than from FamilyHQ, because Google is the system
  of record and the Google Calendar app on the family's phones is what reads the result.

  Background:
    Given the preprod kiosk is open and signed in

  @KG1
  Scenario: A two-member event is written once, to the shared calendar
    When I create an event on the kiosk for two members
    Then Google holds exactly one matching event, on the shared calendar
    And the event's description names both members

  @KG1b
  Scenario: A single-member event is written to that member's calendar only
    When I create an event on the kiosk for one member
    Then Google holds exactly one matching event, on that member's calendar
    And the shared calendar holds no matching event

  @KG3
  Scenario: Editing only the title leaves every other field in Google untouched
    Given an event exists in Google the way a phone creates one
    And the kiosk has received that event
    When I change only that event's title on the kiosk
    Then Google still holds every other field of that event unchanged
    And Google holds the new title

  @KG4
  Scenario: Deleting an event on the kiosk removes it from Google
    Given the kiosk has created an event for one member
    When I delete that event on the kiosk
    Then Google no longer holds the event
