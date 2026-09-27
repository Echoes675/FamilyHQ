@kiosk
Feature: A series edited in Google, the way the phone app edits one

  The Google Calendar app does not have a "scope" the way the kiosk does; it performs each of the three
  choices as a different edit to the data. Singling out one occurrence writes a separate event recorded
  against the slot it replaces. "This and following" truncates the series and inserts a second one for the
  rest. "All events" changes the master — and a change to the master reaches an already-singled-out
  occurrence too, which is what real Google was observed to do rather than what its documentation implies.

  So these scenarios make each of those edits directly, exactly as the app does, and then ask whether the
  kiosk shows what Google now holds. Every change waits for the live push; nothing triggers a sync.

  Background:
    Given the preprod kiosk is open and signed in
    And a bounded weekly series created in Google has reached the kiosk

  @RG4
  Scenario: One occurrence moved and renamed in Google changes on the kiosk and nothing else does
    When the middle occurrence is moved and renamed in Google
    Then Google holds that occurrence alone as an exception at its new time
    And the occurrences preprod serves match Google's expansion, title by title

  @RG5
  Scenario: One occurrence deleted in Google disappears from the kiosk and the others stay
    When the middle occurrence is deleted in Google
    Then the occurrences preprod serves match Google's expansion
    And preprod no longer serves the occurrence that was deleted

  @RG6
  Scenario: A series split in Google at one occurrence shows as both halves on the kiosk
    When the series is split in Google at its middle occurrence
    Then Google holds the original series ending before the split and a second series from it
    And the occurrences preprod serves match Google's expansion, title by title

  @RG7
  Scenario: Renaming a Google series reaches the occurrence that had already been singled out
    Given the first occurrence has been given its own title in Google
    When the series title is changed on the master in Google
    Then that occurrence is still a single exception in Google
    And the occurrences preprod serves match Google's expansion, title by title
