---
name: git-workflow
description: Details processes of branching strategy, commit standards and Pull Request requirements.
---

# Git Workflow

## Branching Strategy
- Main: Production-ready code only.
- Dev: Integration branch for completed and verified features.
- Feature/Fix: feature/description or fix/issue-id.

## Commit Standards
See skill git-commit-formatter

## PR Requirements
- Code must compile via dotnet build.
- All unit tests must pass.
- All E2E tests must pass.
- No commented-out code or Console.WriteLine statements (use ILogger).