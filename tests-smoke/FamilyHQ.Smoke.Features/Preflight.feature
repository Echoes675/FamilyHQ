@preflight
Feature: Preprod environment health

  The smoke suite checks preprod and never repairs it. A bad environment followed by a green run is
  false confidence, so these checks run once at the start of the run and every other feature refuses
  to start until all of them have passed.

  Each check reports what is wrong and what to do about it, because the person reading a red smoke run
  does not yet know whether FamilyHQ is broken or the environment has drifted.

  Background:
    Given preprod's environment health has been checked

  Scenario: Preprod mints a FamilyHQ session token for the smoke account
    Then the "FamilyHQ session token" environment check passes

  Scenario: Preprod refreshes the smoke account's stored Google grant
    Then the "Google access token" environment check passes

  Scenario: The refreshed Google token can read the smoke account's calendars
    Then the "Google calendar list" environment check passes

  Scenario: Preprod has the expected location saved
    Then the "saved location" environment check passes

  Scenario: Preprod resolves the expected family time zone
    Then the "family time zone" environment check passes

  Scenario: The expected calendars exist, with exactly one flagged shared
    Then the "test calendars" environment check passes

  Scenario: Every push-capable calendar has a live Google push channel
    Then the "webhook registrations" environment check passes
