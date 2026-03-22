---
name: git-workflow
description: Details processes of branching strategy, commit standards and Pull Request requirements.
---

# Git Workflow

## Branching Strategy
- Master: Production-ready code only.
- Dev: Integration branch for completed and verified features.
- Feature/Fix: feature/description or fix/issue-id. Usually created from `dev` branch unless told otherwise. Never directly created from `master` unless explicitly told to do so.

## Commit Standards
- See skill git-commit-formatter
- **Never** commit directly to `master` or `dev` branches

## PR Requirements
- Code must compile via dotnet build.
- All unit tests must pass.
- Branch pipeline must be successful.
- All E2E tests must pass on the `FamilyHQ-Deploy-Dev` pipeline. (make sure the build pipeline has been successful first)
- No commented-out code or Console.WriteLine statements (use ILogger).
- PR target is normally the `dev` branch unless told otherwise. **Never** target `master`
