@kiosk
Feature: Reminders, against the real Google API

  Reminders are the field most likely to be damaged without anyone noticing. They are set almost entirely
  in the Google Calendar app on a phone, they are invisible on the kiosk's calendar grid, and the symptom
  of losing one is silence at the moment the family expected to be told about something. Nothing on screen
  says it went wrong.

  Google also does more to a reminder write than accept it. It answers 200 and then rewrites: it clamps an
  offset it thinks is out of range, collapses a duplicate, drops a delivery method it does not know, hands
  an event's reminders back over when the event becomes all-day, and materialises a calendar's own
  reminders onto an all-day event rather than letting it inherit them. Every assertion here is therefore
  made against what Google RETURNED, and every set is compared as a set, because Google reorders them.

  One thing the API cannot see: a reminder that fires after the event has already started. An all-day event
  starts at midnight, so a reminder Google shows in its own apps as "on the day at 09:00" is simply absent
  from the API's answer. No scenario here may read that absence as "this event has no reminders", and none
  of them does.

  Background:
    Given the preprod kiosk is open and signed in

  @RM1
  Scenario: Reminders set in Google survive an edit that changed only the title
    Given an event with two reminders exists in Google the way a phone creates one
    And the kiosk has received that event
    And the kiosk shows the reminders Google holds for that event
    When I change only that event's title on the kiosk
    Then Google still holds that event's reminders, as a set

  # The mirror of RM1, and the half that gets missed. RM1 asks whether an ordinary edit damages the
  # reminders; this asks whether a reminder edit damages everything else. Editing reminders is new with
  # this feature and goes out as a whole event resource, so its blast radius is every other field on an
  # event somebody created on a phone.
  @RM6
  Scenario: A reminder-only edit leaves everything else about the event alone
    Given an event with two reminders exists in Google the way a phone creates one
    And the kiosk has received that event
    When I hand that event's reminders back to its calendar on the kiosk
    Then Google still holds everything else about that event exactly as it was

  @RM2
  Scenario: Reminders chosen on the kiosk reach Google as the set that was chosen
    When I create an event on the kiosk with two reminders of its own
    Then Google holds exactly those reminders on that event, as a set

  @RM3
  Scenario: Handing an event's reminders back to its calendar restores inheritance in Google
    Given the kiosk has created an event with two reminders of its own
    When I hand that event's reminders back to its calendar on the kiosk
    Then Google holds that event as following its calendar's own reminders

  # RM3 drives the inheritance toggle one way — an explicit list handed back to the calendar. This drives
  # it the other, which is the direction a family reported going wrong: an event following its calendar's
  # reminders, taken off them with nothing put in their place. Following the calendar and carrying none of
  # its own are two different states in Google, and this is the only scenario that lands in the second.
  @RM7
  Scenario: Taking an event off its calendar's reminders leaves it carrying none in Google
    Given the kiosk has created an event that follows its calendar's reminders
    When I take that event off its calendar's reminders on the kiosk without adding one
    Then Google holds that event as carrying no reminders of its own

  # This one RECORDS a behaviour nobody has observed, rather than asserting an expected value. Switching an
  # event to all day on the kiosk sends Google a revert-to-default reminder body on an all-day event, and
  # no fixture from the spike covers that: every all-day event it looked at came back with the calendar's
  # reminders materialised, never inheriting. So the scenario asserts only that Google ACCEPTED the write —
  # which is worth gating on, because a rejection would break the All-day toggle for the family — and puts
  # what Google actually holds into the run's output. Inventing an expectation here would either pass
  # vacuously or block every release on a guess. Once the answer is known, the assertion belongs here.
  @RM4
  Scenario: What Google does with a revert-to-default reminder body on an all-day event
    Given an event with two reminders exists in Google the way a phone creates one
    And the kiosk has received that event
    When I switch that event to all day on the kiosk
    Then Google accepts the write, and what it now holds for those reminders is recorded

  @RM5
  Scenario: An all-day event created on the kiosk does not inherit its calendar's reminders
    When I create a one-day all-day event on the kiosk
    Then Google holds that all-day event as not inheriting, and what it applied is recorded
