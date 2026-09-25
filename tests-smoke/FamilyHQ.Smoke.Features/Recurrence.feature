@kiosk
Feature: Recurrence, against Google's own expansion

  Recurrence is checked against Google's `events.instances` and never against a calculation of ours.
  A weekly rule and a locally-derived weekly rule agree in the common case and diverge at a DST
  transition — that is not a difference of opinion, it is a latent bug with a date on it.

  Every series here is bounded to three occurrences. The smoke events are kept after the run for
  post-mortem, so an endless series would keep expanding on a live calendar for ever.

  Background:
    Given the preprod kiosk is open and signed in

  @RK1
  Scenario: A bounded weekly series created on the kiosk matches Google's expansion
    When I create a bounded weekly series on the kiosk
    Then Google holds one series master, bounded weekly, anchored to the family time zone
    And the occurrences preprod serves are exactly Google's instances

  @RG1
  Scenario: A bounded two-weekday series created in Google shows exactly its instances
    When a bounded weekly series on two weekdays is created in Google
    And the kiosk has received that event
    Then the occurrences preprod serves are exactly Google's instances
    And each occurrence preprod serves is at the same wall-clock time as Google's
    And the kiosk marks those occurrences as recurring
