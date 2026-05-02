---
name: git-workflow
description: Details processes of branching strategy, commit standards and Pull Request requirements.
---

# Git Workflow

## Branching Strategy
- Master: Production-ready code only.
- Dev: Integration branch for completed and verified features.
- Feature/Fix: feature/description or fix/issue-id. Usually created from `dev` branch unless told otherwise. Never directly created from `master` unless explicitly told to do so.

## Versioning — when to bump MAJOR / MINOR

The PATCH number auto-increments on every master merge via Jenkins (see `.agent/docs/ci-cd.md`). MAJOR and MINOR are controlled manually by editing `<MinVerMinimumMajorMinor>` in `Directory.Build.props`.

- **PATCH (auto)** — every master merge produces `v{M}.{m}.{patch+1}`. No action required.
- **MINOR** — bump when shipping a noticeable new capability (a new page, a new integration, a redesign worth marking). Edit `<MinVerMinimumMajorMinor>` from `1.0` to `1.1` (etc.) in a normal feature-branch PR alongside the feature itself. The next master merge will produce `v{M}.{newMinor}.0`.
- **MAJOR** — bump rarely: breaking changes, coordinated DB schema migrations, or rewrites worth flagging. Same edit mechanism as MINOR.

Default behaviour: leave MAJOR/MINOR alone. `v1.0.247` is a perfectly valid release. Only bump when the version line should tell a story.

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

> Use the `ci-gate` skill to satisfy the pipeline and E2E requirements above.
