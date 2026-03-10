---
name: bdd-testing
description: A set of rules and best practices that guide on how to write, structure, and format end to end behaviour driven development tests. Used when writing new or editing existing tests.
---

# BDD Writing Standards

This document defines what good Gherkin looks like for the BillerGenie test suite.
Read this alongside the Recording Guide before generating any feature files.

The Item Management feature is the reference example of correct style.
The Customer Management feature is the reference example of what to avoid.

## Stack
- Framework: Reqnroll using Gherkin for feature files.
- Assertions: FluentAssertions
- Tests should be written to verify and confirm externally observable behaviour.
- All tests must pass before a feature can be confirmed as complete.
- Tests should be written before implementation and then used to verify that the desired behaviour has been achieved
-- **DO NOT** write tests based on implementation, but rather should be based on the desired behaviour.

## File structure
- tests-e2e/FamilyHQ.E2E.Data/:		Contains the test data required by the tests.
- tests-e2e/FamilyHQ.E2E.Common/:	Contains common classes, models, services, hooks required to facilitate E2E tests.
- tests-e2e/FamilyHQ.E2E.Steps/:	Contains steps that make up the tests.
- tests-e2e/FamilyHQ.E2E.Features/:	Contains feature files.
- tests-e2e/FamilyHQ.E2E.*/:		Other projects as required to maintain strict separation of concerns.

## The Golden Rule

Scenarios describe **observable business outcomes**, not UI interactions or test setup.

A non-technical stakeholder — a product manager, a QA lead — should be able to read
a scenario and understand exactly what the system does, without knowing anything about
the UI or the test framework.

## Rule 1: Test Isolation
Scenarios should be insulated from each other. They should not be re-using or depending on anything that other tests do. For example:
Each test should create an isolated user as the first 'Given' step, which calls the Simulator and passes the required user template object from the FamilyHQ.E2E.Data project

```gherkin
Scenario: Create a new customer
  Given I have a calendar user like "Calendar User 2"
```

Example template in `user_templates.json`:

```json
[
	{
		"Name": "Calendar User 2",
		"Calendars": [
		{
			"CalendarName": "Work"
		},
		{
			"CalendarName": "Personal"
		}
		]
	}
]
```

The isolated user should be held in memory for the duration of the test, isolated from all other tests and accessible by any step that needs.

## Rule 2: Step Definition Patterns Must Use `[^""]` Not `(.*)` for Quoted Arguments

When writing Reqnroll step patterns that capture quoted arguments, always use `([^"]*)`
instead of `(.*)` inside the quotes.

`(.*)` is greedy. Given the step:
```
I have a calendar user like "Premium Calendar User" with "premium" plugin
```

The pattern `I have a calendar user like "(.*)"` will match it — `(.*)` greedily captures
`Premium Calendar User" with "premium` all the way to the last `"`. Reqnroll then
sees two patterns both matching the same step and raises an **Ambiguous step definitions**
error at runtime.

`([^"]*)` means "any character except a quote", so it stops at the first closing `"` and
cannot bleed across quote boundaries.

**Wrong — ambiguous when more specific patterns exist:**
```csharp
[Given(@"I have a calendar user like ""(.*)""")]
```

**Right — stops at the first closing quote:**
```csharp
[Given(@"I have a calendar user like ""([^""]*)""")]
```

This applies to every quoted capture group in every step pattern, not just calendar user steps.
In C# verbatim strings (`@"..."`), `""` is an escaped `"` — so `[^""]` in the source
produces the regex `[^"]`.

---

## Rule 3: Use Background for Shared Preconditions

If every scenario in a feature starts with the same `Given` steps, those steps belong
in a `Background` block — not repeated in each scenario.

**Wrong:**
```gherkin
Scenario: Create a new customer
  Given I have a calendar user like "Plugin Calendar User"
  When I create a customer named "Jane Smith" with email "jane@example.com"
  Then I see "Jane Smith" in the customers list

Scenario: Edit a customer's details
  Given I have a calendar user like "Plugin Calendar User"
  And a customer "Jane Smith" exists
  When I update the customer email to "jane.new@example.com"
  Then I see the updated email "jane.new@example.com" on the customer record
```

**Right:**
```gherkin
Background:
  Given I have a calendar user like "Premium Calendar User"

Scenario: Successfully create a new customer
  When I create a customer with a name and email
  Then I see the customer in the customers list

Scenario: Edit a customer's details
  Given a customer exists
  When I update the customer email address
  Then I see the updated email address on the customer record
```

---

## Rule 4: Be Specific — Use Real Domain Values

Vague steps cannot be meaningfully automated or read. Always include the specific
values that define the scenario: names, amounts, statuses, quantities.

**Wrong:**
```gherkin
When I create a customer with a name and an email
Then I see a customer with that name and email in the customers list
```

**Right:**
```gherkin
When I create a customer with a name and email
Then I see the customer in the customers list
```

**Wrong:**
```gherkin
When I create an invoice with some items
Then the invoice total is correct
```

**Right:**
```gherkin
When I create an invoice with an item and a price
Then the invoice total reflects the item price
```

The reference for this is Item Management:
```gherkin
When I create an item "Oil Filter" with price $25.00
Then I see the item "Oil Filter" in the items list
```
Specific. Testable. Readable.

---

## Rule 4b: Only Include Values That Are Relevant to the Outcome

A value belongs in a scenario only if it is directly relevant to what is being asserted.
Ask: if this value changed, would the test catch something different? If not, leave it out.

**Wrong — name is irrelevant to the outcome:**
```gherkin
When I create a customer named "Jane Smith" with email "jane@example.com"
Then I see "Jane Smith" in the customers list
```

The customer name has no bearing on whether creation works. The email is the meaningful
identifier. The name is noise that will need to be maintained for no reason.

**Right — only the values that matter:**
```gherkin
When I create a customer with a name and email
Then I see the customer in the customers list
```

**Contrast with Item Management — here the values DO matter:**
```gherkin
When I create an item "Oil Filter" with price $25.00
Then I see the item "Oil Filter" in the items list
```

The item name and price are asserted directly in the `Then` step — they are the point
of the test. Include values when you assert them. Omit values when you do not.

**The test to apply before including any value:**
Does the assertion reference this value? If yes, include it. If no, leave it out.

---

## Rule 5: Each Scenario Must Test Something Distinct

Do not write two scenarios that cover the same behaviour with minor wording differences.
Every scenario should have a different outcome or a different system state.

**Wrong — these test the same thing:**
```gherkin
Scenario: View a customer's details
  Given a customer exists with a name and email
  When I view the details for that customer
  Then I see the customer name
  And I see the customer email

Scenario: Edit a customer's details
  Given a customer exists with a name and email
  When I edit the details for that customer
  Then I see a customer with that name and email in the customers list
```

The "view" scenario adds no value — the "edit" scenario already requires viewing.
Replace with scenarios that have genuinely different outcomes:

```gherkin
Scenario: Edit a customer's email address
  Given a customer exists
  When I update the customer email address
  Then I see the updated email address on the customer record

Scenario: Attempt to create a customer without an email address
  When I attempt to create a customer named "No Email" without an email address
  Then I am informed that an email address is required
```

---

## Rule 6: Max 5 Steps Per Scenario (Excluding Background)

If a scenario needs more than 5 steps, it is doing too much. Split it or move
precondition setup into Background or a manager.

## Rule 7: Aim for 3–5 Scenarios Per Feature

Cover the meaningful cases — do not pad with scenarios that add no new information.

| Scenario type | Example |
|---|---|
| Happy path | Create a customer successfully |
| Variation on happy path | Edit a customer's details |
| State change | Set an item to inactive |
| Validation / error | Attempt to create without required field |
| Boundary / edge case | Attempt to pay an already-paid invoice |

The Item Management feature does this well:
- Create (happy path)
- Update (variation)
- Set inactive (state change)

What is missing and would round it out:
- Attempt to create an item without a name (validation)

---

## Rule 8: Describe What the System Does, Not How the UI Works

Scenarios must survive a UI redesign. If a button is renamed or a form is restructured,
the scenario should not need to change.

**Wrong (UI-coupled):**
```gherkin
When I click the "Add Customer" button
And I fill in the "Full Name" field with "Jane Smith"
And I fill in the "Email" field with "jane@example.com"
And I click "Save"
```

**Right (behaviour-focused):**
```gherkin
When I create a customer with a name and email
```

The how is implemented in the step definition and page object. The scenario only
describes the intent.

---

## Rule 9: Then Steps Must Assert on Page Content — Not Just URLs

A `[Then]` step must verify an **observable business outcome** on the page — a visible
element, a status badge, a message, a value in the DOM. Checking the URL alone is not
an assertion; it only confirms the browser navigated somewhere, not that the operation
succeeded.

Every `[Then]` step that navigates to a new page must include at least one DOM assertion
that could only pass if the operation actually succeeded:

| Outcome | Acceptable assertion |
|---|---|
| Transaction recorded (Approved) | `label.label-success` badge is visible |
| Transaction voided | `dl dd:has-text('Card Void')` is visible |
| Invoice paid | "Send Paid Reminder" button is visible (only rendered when `IsPaid == true`) |
| Validation error shown | Error message element is visible |
| Record deleted | Record no longer appears in the list |

**The rule:** If swapping your `[Then]` implementation for a `Task.CompletedTask` would
still pass because the URL happened to match — the assertion is wrong. Fix it.

URL waits are fine as a synchronisation mechanism before the DOM check, but they do not
count as the assertion.

---