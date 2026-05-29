Feature: Sync resilience and diagnostics
  As a family member
  I want the app to surface Google-side failures and keep syncing what it can
  So that one broken event or revoked token does not leave me stranded

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"

  Scenario: Reauth banner appears when Google revokes the refresh token
    Given the user's Google refresh token has been revoked
    When I trigger a manual sync from the Settings page
    Then I see the reauth banner on the dashboard with a reconnect call to action

  Scenario: Reauth banner shows the Google-supplied reason when Calendar API returns 403
    Given the Google Calendar API will return a 403 for the user
    When I trigger a manual sync from the Settings page
    Then I see the reauth banner on the dashboard
    And the banner shows the reason "Forbidden"

  Scenario: Diagnostics page shows needs-reauth status with reconnect button
    Given the user's Google refresh token has been revoked
    When I trigger a manual sync from the Settings page
    And I view the diagnostics page
    Then the connection status badge reads "Needs Reauth"
    And I see a reconnect button on the diagnostics page

  Scenario: Diagnostics page lists a sync event failure when one event in a sync throws
    Given the user has an all-day event "Soccer practice" tomorrow
    And a sync event failure has been recorded
    When I view the diagnostics page
    Then I see the failure in the recent sync failures table
    And my other events still appear on the dashboard

  Scenario: Diagnostics page lists a failed sync run when a webhook-driven sync fails terminally
    Given the user's Google refresh token has been revoked
    When Google Calendar sends a webhook notification
    And I wait for the failed sync run to be recorded
    And I view the diagnostics page
    Then I see the failed run in the recent failed sync runs table
