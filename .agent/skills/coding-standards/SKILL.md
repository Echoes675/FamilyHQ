---
name: coding-standards
description: A set of rules and best practices that guide on how to write, structure, and format code. Used when writing new or editing existing code.
---

# C# & Coding Standards

## Modern C# Preferences
- Namespaces: Always use file-scoped namespaces (e.g., namespace FamilyHQ.Core;).
- Constructors: Prefer Primary Constructors for classes with simple dependency injection.
- Expression-bodied members, Pattern matching, Null-coalescing operators, and Collection expressions.
- Nullability: Nullable Reference Types (NRT) are enabled.
- Use ? for optional data.
- Use null! for properties initialized via EF Core or DI containers where you are certain they won't be null at runtime.
- See skill @dotnet-backend-patterns

## Naming Conventions
|    Construct   |        Convention        |        Example       |
|:--------------:|:------------------------:|:--------------------:|
| Class          | PascalCase               | InvoiceService       |
| Interface      | PascalCase with I prefix | IInvoiceService      |
| Method         | PascalCase               | ProcessPayment       |
| Property       | PascalCase               | MerchantId           |
| Constant       | PascalCase               | MaxRetryCount        |
| Local variable | camelCase                | invoiceTotal         |
| Parameter      | camelCase                | merchantId           |
| Private field  | camelCase with _ prefix  | _logger, _retryCount |

- Names must describe intent, not implementation.
- Avoid abbreviations unless universally understood (e.g., Id, Url).

## Implementation Patterns
- One class per file — the file name must match the class name exactly.
- Async/Await: Use Async suffix for all asynchronous methods. Never use .Result or .Wait().
- Dependency Injection: Use constructor injection exclusively. Avoid IServiceProvider (Service Locator pattern).
- Member Order:
1. Constants
2. Fields 
3. Constructors
4. Properties
5. Public Methods
6. Private/Internal Methods
- Uphold separation of concerns. Insulate internal business logic from implementation details in presentation layers.
-- Never use internal core models as data objects used to receive or send data.
-- Views in the UI should make use of specific ViewModels and not core models to populate views or send data.
- Error Handling: Implement a Global Exception Filter or Middleware to catch unhandled exceptions. Never return stack traces or internal exception details to the client.
- Web Safety: Ensure CORS policies are restricted to known origins. Implement Anti-Forgery tokens for any state-changing operations (POST/PUT/DELETE) if not using pure JWT.

## Logging
- Use structured logging (e.g., _logger.LogInformation("Processing event {EventId}", eventId)).
- Never use string interpolation inside log templates.
- Log sensitive data (PII, tokens).