# FamilyHQ Agent Instructions

Dashboard app that displays a family calendar events

## Core Context

- **Framework**: .NET 10 (Blazor WASM Frontend, ASP.NET Core Backend)
- **Database**: PostgreSQL / EF Core
- **Primary Tooling**: dotnet CLI (build, test, run)
- **E2E Acceptance testing**: Read `.agent\docs\e2e-testing-maintenance.md`

## Rules of Engagement (Safety)

- Operations Allowed Without Prompting
-- Read files, list directory contents
-- Type check, lint, format single files
-- Run single unit test
-- Search codebase, read documentation
-- Create git branches and commits

- Operations That Require Approval
-- Installing new packages or dependencies
-- Modifying configuration files (package.json, tsconfig.json, etc.)
-- Running full project build
-- Running full test suite or E2E tests
-- Git push operations
-- Deleting files or directories
-- Modifying database schemas
-- Changing environment variables
-- Making commits directly on the 'dev' or 'main' branches

## Progressive Disclosure Links
Refer to these files in the .agent/ directory for specific implementation details:
- Project Architecture & Structure: `.agent/docs/architecture.md`

## Skills
Read the relevant skill file before starting any task of that type:

- **Git commits**: Read `.agent/skills/git-commit-formatter/SKILL.md`
- **Git workflow (branching, PRs)**: Read `.agent/skills/git-workflow/SKILL.md`
- **Writing/modifying backend C# code**: Read `.agent/skills/dotnet-backend-patterns/SKILL.md`
- **Working with DateTimeOffset and PostgreSQL**: Read `.agent/skills/datetimeoffset-postgresql/SKILL.md`
- **Writing or modifying tests**: Read `.agent/skills/testing-standards/SKILL.md` and `.agent/skills/test-driven-development/SKILL.md`
- **BDD/acceptance tests**: Read `.agent/skills/bdd-testing/SKILL.md`
- **Security-sensitive code**: Read `.agent/skills/security/SKILL.md`
- **Any code changes**: Read `.agent/skills/coding-standards/SKILL.md`
- **Error handling or validation**: Read `.agent/skills/fail-fast-standard/SKILL.md`
- **Frontend UI development**: Read `.agent/skills/frontend-design/SKILL.md`