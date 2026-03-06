# FamilyHQ Agent Instructions

Dashboard app that displays a family calendar events

## Core Context

- **Framework**: .NET 10 (Blazor WASM Frontend, ASP.NET Core Backend)
- **Database**: PostgreSQL / EF Core
- **Primary Tooling**: dotnet CLI (build, test, run)

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

## Progressive Disclosure Links
Refer to these files in the .agents/ directory for specific implementation details:
- Project Architecture & Structure: .agent/architecture.md
- Skills: .agent/skills