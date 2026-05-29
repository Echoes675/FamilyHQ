Feature: Recurring Event Members
  As a family member
  I want recurring events shared between members to behave like single events
  So that membership is preserved across the whole series and only changed series-wide

  Background:
    Given I have a user like "MultiCalUser"
    And the "Family Calendar" calendar is the active calendar
    And I login as the user "MultiCalUser"
    And I view the dashboard
    And I switch to the Day View tab

  Scenario: A multi-member series syncs with every instance linked to all members
    Given the user has a weekly recurring event "Family movie night" for 3 occurrences shared between "Work Calendar" and "Personal Calendar"
    When Google Calendar sends a webhook notification
    Then occurrence 1 of "Family movie night" is linked to member "Work Calendar"
    And occurrence 3 of "Family movie night" is linked to member "Personal Calendar"

  Scenario: Adding a member at All events scope links every instance to both members
    Given the user has a weekly recurring event "Yoga" for 3 occurrences on "Personal Calendar"'s personal calendar
    When Google Calendar sends a webhook notification
    And I add member "Work Calendar" to occurrence 1 of "Yoga" applying to all events
    Then occurrence 1 of "Yoga" is linked to member "Work Calendar"
    And occurrence 3 of "Yoga" is linked to member "Personal Calendar"

  Scenario: A member change is refused at This event scope
    Given the user has a weekly recurring event "Standup" for 3 occurrences on "Personal Calendar"'s personal calendar
    When Google Calendar sends a webhook notification
    Then adding member "Work Calendar" to occurrence 2 of "Standup" is refused at this-event scope
