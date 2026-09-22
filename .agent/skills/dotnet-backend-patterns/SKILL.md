---
name: dotnet-backend-patterns
description: "Master C#/.NET backend development patterns for building robust APIs, MCP servers, and enterprise applications. Covers async/await, dependency injection, Entity Framework Core, Dapper, configuration..."
risk: unknown
source: community
date_added: "2026-02-27"
---

# .NET Backend Development Patterns

Master C#/.NET patterns for building production-grade APIs, MCP servers, and enterprise backends with modern best practices (2024/2025).

## Use this skill when

- Developing new .NET Web APIs or MCP servers
- Reviewing C# code for quality and performance
- Designing service architectures with dependency injection
- Implementing caching strategies with Redis
- Writing unit and integration tests
- Optimizing database access with EF Core or Dapper
- Configuring applications with IOptions pattern
- Handling errors and implementing resilience patterns

## Do not use this skill when

- The project is not using .NET or C#
- You only need frontend or client guidance
- The task is unrelated to backend architecture

## Instructions

- Define architecture boundaries, modules, and layering.
- Apply DI, async patterns, and resilience strategies.
- Validate data access performance and caching.
- Add tests and observability for critical flows.
- If detailed patterns are required, open `resources/implementation-playbook.md`.

## Required question for any new owned or JSON-mapped property

Before adding `OwnsOne`, `OwnsMany` or `ToJson()` to an entity, ask:

> **Does this repository ever save this entity DETACHED?**

Grep the repository for `AsNoTracking` reads of it followed by `Update(...)`. If the answer is yes —
and for `CalendarInfo` and `CalendarEvent` it is — do not use an owned collection. EF gives every
owned-collection element a shadow key (`__synthesizedOrdinal`) that exists only on a tracked entity,
so `SaveChanges` throws `"The value of shadow key property … is unknown when attempting to save
changes"`, and a detached *clear* of the property silently does not clear. FHQ-205 stopped all
production calendar syncing for five hours this way.

Map a **value** (no identity of its own, always read and written whole) with `HasConversion` plus a
structural `ValueComparer` and an explicit `HasColumnType`, so EF treats the column as one opaque
scalar. See `EventRemindersConversion`. A comparer is not optional: without one EF compares by
reference and never detects a change, and its hash must agree with its equality or updates go
missing. Its snapshot must deep-copy, or a mutation of the collection is invisible.

Adding such a property to an entity that was already saved detached is the risky case — a scalar has
no shadow key, so the existing save path gives no warning that it is about to stop working.

## Resources

- `resources/implementation-playbook.md` for detailed .NET patterns and examples.