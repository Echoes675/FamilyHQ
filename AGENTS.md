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
- UI Design System (themes, CSS variables, layer model, touch rules): `.agent/docs/ui-design-system.md`
- Intermittent / flaky issues tracker (read before dismissing a CI failure as flake): `.agent/docs/intermittent-issues.md`

## Skills
Read the relevant skill file before starting any task of that type:

- **Git commits**: Read `.agent/skills/git-commit-formatter/SKILL.md`
- **Git workflow (branching, PRs)**: Read `.agent/skills/git-workflow/SKILL.md`
- **Writing/modifying backend C# code**: Read `.agent/skills/dotnet-backend-patterns/SKILL.md`
- **Working with DateTimeOffset and PostgreSQL**: Read `.agent/skills/datetimeoffset-postgresql/SKILL.md`
- **Writing or modifying tests**: Read `.agent/skills/testing-standards/SKILL.md` and `.agent/skills/test-driven-development/SKILL.md`
- **BDD/acceptance tests**: Read `.agent/skills/bdd-testing/SKILL.md`
- **Playwright/browser automation**: Read `.agent/skills/playwright-cli/SKILL.md`
- **Security-sensitive code**: Read `.agent/skills/security/SKILL.md`
- **Any code changes**: Read `.agent/skills/coding-standards/SKILL.md`
- **Error handling or validation**: Read `.agent/skills/fail-fast-standard/SKILL.md`
- **Frontend UI development**: Read `.agent/skills/frontend-design/SKILL.md`
- **Any CSS, component, layout, or page changes**: Read `.agent/skills/ui-theming/SKILL.md` (project-specific theme rules — takes precedence over frontend-design for colour and animation decisions)
- **Pushing a branch and verifying CI (before raising a PR)**: Read `.agent/skills/ci-gate/SKILL.md`

### Skill Registration Rule
When creating a new skill:
1. Create the skill directory and `SKILL.md` file in `.agent/skills/`.
2. Update this "## Skills" section to include the new skill.
3. Ensure the skill follows the standard format with clear triggers and instructions.
4. Skills are automatically discovered at runtime using `list_files(".agent/skills", recursive=true)`.