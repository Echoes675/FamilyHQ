Feature: Version footer and auto-reload
  Visibility of the running build version, and automatic reload when a new version is deployed.

  Background:
    Given I have a user like "TestFamilyMember"
    And I login as the user "TestFamilyMember"

  Scenario: Footer displays a SemVer-shaped version on the dashboard
    When I view the dashboard
    Then the footer should display a version matching the SemVer pattern

  Scenario Outline: Footer is visible on every primary page
    When I navigate to the "<page>" page
    Then the footer should display a version matching the SemVer pattern

    Examples:
      | page      |
      | dashboard |
      | settings  |

  Scenario: /api/health returns a non-empty SemVer version field
    When I request the health endpoint
    Then the response should contain a version matching the SemVer pattern
    And the response should set Cache-Control to "no-store"

  @ignore
  # TODO: Re-enable once SignalR reconnect can be triggered deterministically
  # from a Playwright test without forcing the WASM ClientVersion to differ
  # from the mocked /api/health response in a stable way. The route override
  # used to fake a "new" server version stays installed across the auto-reload
  # cycle, which means the post-reload VersionService instance keeps seeing a
  # mismatch and re-triggers the banner — making the "after reload" assertion
  # racy. See plan: i-want-to-have-federated-marshmallow.md, Phase 6.
  Scenario: A new server version triggers the update banner and auto-reload
    Given the dashboard is open and the footer shows the current version
    When the server reports a new version after a SignalR reconnect
    Then the update banner should appear within 2 seconds
    And the page should reload within 8 seconds
    And after reload the footer should display the new version
