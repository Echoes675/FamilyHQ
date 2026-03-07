---
name: testing-standards
description: A set of rules and best practices that guide on how to write, structure, and format unit tests. Used when writing new or editing existing tests.
---

# Testing Guidelines
## Stack
- Framework: xUnit
- Mocking: Moq
- Assertions: FluentAssertions
- Time: Utilize FakeTimeProvider for any logic involving DateTime.
- Do not test private methods directly, in stead verify the observable behavior of the class.
- Write tests alongside implementation

## Test structure
- Project Organization: Test projects must reside in a dedicated tests/ directory.
- Each test project must match the namespace of the project under test with the suffix .Tests (e.g., FamilyHQ.Data.Tests).

## Design Rules
- Pattern: Follow Arrange-Act-Assert (AAA).
- Naming: [MethodName]_[Scenario]_[ExpectedResult].
- Isolation: Mock any external dependency using Moq. Tests should not hit real databases or external APIs.
-- Do not use databases, files, or environment state unless explicitly required
- Coverage: Aim for 80% coverage on new business logic in FamilyHQ.Services.
- Deterministic Tests: No DateTime.Now (use TimeProvider) and no Guid.NewGuid() (use static, predictable GUIDs).
- Verification Rule: Use mock.Verify() only when the interaction itself is the behavior being tested. Otherwise, use state verification.
- Mocked dependencies should not be shared across tests. Static helper methods should be used to generate mocked dependencies for any test that needs them.