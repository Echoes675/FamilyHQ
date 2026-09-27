@kiosk
Feature: Deleting from a series on the kiosk, at each scope

  A delete is the one operation with nothing to undo it. Applied at the wrong scope it takes occurrences
  the family never offered up, and it takes them from the system of record, where the phone app will show
  the hole and the kiosk will not.

  The three scopes are three different writes to Google: one cancels a single occurrence, one ends the
  series just before it, and one removes the series outright. What is left afterwards is checked against
  Google's own expansion, because a delete that succeeds locally and leaves the event in Google is the worst
  shape of the bug — it comes back on the next sync.

  Background:
    Given the preprod kiosk is open and signed in
    And the kiosk has created a bounded weekly series

  @RK8
  Scenario: Deleting one occurrence cancels that occurrence alone
    When I delete the middle occurrence on the kiosk for this event only
    Then Google's expansion no longer includes that occurrence but still includes the others
    And the occurrences preprod serves match Google's expansion

  @RK9
  Scenario: Deleting this and following ends the series before that occurrence
    When I delete the middle occurrence on the kiosk for this and following events
    Then Google holds the series ending before that occurrence
    And the occurrences preprod serves match Google's expansion

  @RK10
  Scenario: Deleting all events removes the series from Google
    When I delete the middle occurrence on the kiosk for all events
    Then Google holds nothing at all for this scenario
    And preprod serves nothing for this scenario
