---
name: git-workflow
description: Details processes of branching strategy, commit standards and Pull Request requirements.
---

# Git Workflow

## Branching Strategy
- Main: Production-ready code only.
- Dev: Integration branch for completed and verified features.
- Feature/Fix: feature/description or fix/issue-id. Usually merged into `dev` branch unless told otherwise.

## Commit Standards
See skill git-commit-formatter

## PR Requirements
- Code must compile via dotnet build.
- All unit tests must pass.
- All E2E tests must pass on the `FamilyHQ-Deploy-Dev` pipeline.
- No commented-out code or Console.WriteLine statements (use ILogger).
- PR target is normally the `dev` branch unless told otherwise. **Never** target `main`
