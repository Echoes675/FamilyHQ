---
name: fail-fast-Standard
desc: emphasizes detecting failures immediately rather than letting errors propagate. FOr consideration when working with end to end BDD testing tests should fail fast so that the point of failure can be easily identified.
---
# Fail Fast Standard

## The Core Principle

Tests should fail loudly, immediately, and at the point of failure.

A test that swallows an exception and continues is a test that lies. It may
report a pass when the system is broken, or fail at a later step with a
misleading error that sends the developer to the wrong place.

**Let exceptions propagate. Do not catch them unless you have a specific,
justified reason to handle them.**

---

## No try/catch in Step Definitions

Step definitions must never wrap Playwright or page object calls in try/catch.
If a step fails, Reqnroll needs to see the raw exception to report the correct
failure location, screenshot, and trace.

**Wrong — hides the real failure, reports at the wrong step:**
```csharp
[When(@"I create an event")]
public async Task WhenICreateAnEvent()
{
    try
    {
        await _eventPage.CreateEvent();
    }
    catch (Exception ex)
    {
        _logger.Error($"Failed to create event: {ex.Message}");
    }
}
```

**Right — exception propagates, test fails at the correct step with full trace:**
```csharp
[When(@"I create an event")]
public async Task WhenICreateAnEvent()
{
    await _eventPage.CreateEvent();
}
```

---

## No try/catch in Page Objects

Page objects are thin interaction layers. They should not catch exceptions from
SDK helpers or Playwright. If a locator times out or a click fails, that is
a test failure — not something to recover from silently.

**Wrong:**
```csharp
public async Task SubmitEvent()
{
    try
    {
        await Interactions.Click(_page, SubmitButton);
    }
    catch
    {
        // button might not be visible yet, try again
        await Task.Delay(1000);
        await Interactions.Click(_page, SubmitButton);
    }
}
```

**Right — if the button isn't visible, the test should fail and tell you why:**
```csharp
public async Task SubmitEvent()
{
    await Interactions.Click(_page, SubmitButton);
}
```

If timing is genuinely the problem, use the SDK `Waits` helpers explicitly
before the action — do not hide it in a catch block.

---

## When try/catch Is Acceptable

There are narrow, legitimate uses. Each must have a clear justification comment.

### 1. Expected error state verification

When a test is explicitly asserting that an operation fails, catching the
exception is the mechanism — not the cover-up:

```csharp
// Verifying that duplicate submission is rejected
try
{
    await _eventPage.SubmitEvent();
    Assert.Fail("Expected an exception for duplicate event submission");
}
catch (PlaywrightException ex) when (ex.Message.Contains("already exists"))
{
    // Expected — test passes
}
```

### 2. Setup/teardown cleanup that must not mask the real failure

In `[AfterScenario]` hooks, cleanup failures should be logged but not allowed
to override the original test failure:

```csharp
[AfterScenario]
public async Task AfterScenario()
{
    try
    {
        await _page.CloseAsync();
    }
    catch (Exception ex)
    {
        // Log only — do not rethrow, original failure is more important
        _logger.Warning($"Page close failed during teardown: {ex.Message}");
    }
}
```

### 3. Manager setup with a meaningful fallback

Occasionally a `[BeforeTestRun]` manager may need to handle a known recoverable
condition. This must be explicit and narrow — never a blanket catch:

```csharp
// Employee may already have a password if test data was not cleaned up
try
{
    await employeeManager.SetInitialPasswordAsync(employee);
}
catch (PasswordAlreadySetException)
{
    _logger.Warning("Employee password already set — continuing with existing credentials");
}
```

---

## The Multi-Catch Rule

If you find yourself writing multiple catch blocks, or a catch block longer
than two lines, stop. This is the same signal as the multi-line comment rule —
the code is doing too much or the wrong thing is being caught.

---

## No Silent Catches

A catch block that does nothing, or only logs, and then allows execution to
continue as if nothing happened is always wrong in a test context:

**Wrong:**
```csharp
catch (Exception ex)
{
    _logger.Error(ex.Message);
    // continue...
}
```

If you catch and log but do not rethrow, the test will pass through a failure
state and produce a false result. Either rethrow, or do not catch.

---

## Summary

| Situation | Exception handling? |
|---|---|
| Step definition action fails | No — let it propagate |
| Page object interaction fails | No — let it propagate |
| Asserting an error state | Yes — narrow typed catch |
| AfterScenario cleanup | Yes — log only, do not rethrow |
| Manager recoverable condition | Yes — narrow typed catch with comment |
| Blanket `catch (Exception)` | Never |
| Silent catch (log and continue) | Never |
| Multi-line catch block | Stop — refactor instead |