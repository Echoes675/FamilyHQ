# Architecture & Structure

## Project Layout
- src/FamilyHQ.WebUi.Web/: Blazor WASM UI.
- src/FamilyHQ.WebApi.Web/: ASP.NET Core API.
- src/FamilyHQ.Services/: Business logic and orchestration.
- src/FamilyHQ.Data/: EF Core context
- src/FamilyHQ.Data.PostgreSQL/: PostgreSQL specific implementation and migrations.
- src/FamilyHQ.Core/: Shared Models, DTOs, and FluentValidation logic.

## Dependency Rules
- Directional Flow: Dependencies must flow inward.
-- WebUi and WebApi -> Services -> Data -> Core.
-- Forbidden: Never add references from Core or Services back to the Web projects.
- Shared Logic: All DTOs, Enums, and Constants used by both Client and Server must reside in FamilyHQ.Core.

## Technical Principles
- Clean Architecture: Ensure the WebApi and WebUi projects only depend on Services or Core.
- Infrastructure Isolation: External integrations (e.g., Google Calendar) must be abstracted behind interfaces.
- Shared Validation: Use FluentValidation in FamilyHQ.Core so it can be executed on both the Blazor client and the ASP.NET server.

##Performance Targets
- Responsiveness: API endpoints should target < 200ms response time.
- EF Core Efficiency:
-- Use AsNoTracking() for read-only queries.
-- Avoid N+1 issues by using .Include() for required navigation properties.
-- Always implement pagination for list-based endpoints using Skip and Take.
-- Async Execution: Always pass CancellationToken from the Controller through to EF Core async methods (e.g., ToListAsync(ct)).
-- Transactions: Use explicit transactions (IDbContextTransaction) for operations involving multiple SaveChangesAsync calls to ensure atomicity.
- Blazor Optimization: Use @key in loops to help the diffing engine and avoid unnecessary re-renders of heavy components.