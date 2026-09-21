---
name: git-workflow
description: Details processes of branching strategy, commit standards and Pull Request requirements.
---

# Git Workflow

## Branching Strategy
- **`master`** — production. Built and deployed. Never committed to directly, never the target of feature work. Exactly two things merge into it: the release PR that ships `dev`, and a `master`-cut hotfix.
- **`dev`** — integration branch for completed and verified features. PRs target it by default.
- **`feat/*` / `fix/*` / `chore/*` / `spike/*`** — created from `dev`, merged back to `dev`. **Never** created from `master`.
- **`hotfix/*`** — created from `dev` **or** from `master`, and merged back into whichever it was cut from. The `master`-cut path is the sole exception to "nothing targets `master` but the release PR", and it is conditional — see below.

Branch naming when a ticket exists: `<feat|fix|chore|spike>/FHQ-N-<short-slug>`, or `hotfix/FHQ-N-<short-slug>`. See the `obsidian-tickets` skill.

### The two hotfix paths

The choice is one question: **is everything currently on `dev` safe to go to production?**

| | `dev` is safe to ship | `dev` is not safe to ship (unfinished, or carrying risk you would not take right now) |
|---|---|---|
| Cut from | `dev` | `master` |
| CI gate | pre-PR gate (3 green `FamilyHQ-Deploy-Dev` runs) | same |
| Staging | automated `dev` run after merge | **manual branch run before the PR** — `jk run start FamilyHQ-Deploy-Staging -p BRANCH=<branch>` |
| PR base | `dev` | `master` |
| After merge | ships with the next release PR | `master` build → staging → preprod → production |
| Then | nothing further | back-merge `master` → `dev` by PR, then wait for the automated `dev` staging run before the next release PR |

Both paths use the same `hotfix/*` name, so **the name never proves the base — the merge-base does**:

```sh
git fetch origin
git log --oneline HEAD..origin/master   # empty → cut from master → PR base master is legitimate
git log --oneline HEAD..origin/dev      # empty → cut from dev  → PR base dev
```

### The back-merge check

Outside a `master`-cut hotfix, nothing reaches `master` that did not come through `dev`, so `dev`
should never be missing *content* that `master` has. Ask git what a back-merge would actually do,
rather than comparing commit lists:

```sh
git fetch origin
tree=$(git merge-tree --write-tree origin/dev origin/master) \
  && [ "$tree" = "$(git rev-parse origin/dev^{tree})" ] \
  && echo "OK - dev already has everything master has" \
  || { echo "BACK-MERGE OWED:"; git diff --stat "$(git rev-parse origin/dev^{tree})" "$tree"; }
```

`BACK-MERGE OWED` means either a `master`-cut hotfix is mid-flight and still owes its back-merge, or
something reached `master` by mistake. Both need the same back-merge into `dev`, and both need it
immediately — skipping it means the next release silently reverts that change. A merge conflict makes
`merge-tree` exit non-zero, which takes the same branch and needs resolving by hand. Needs git 2.38+.

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
- PR target is the `dev` branch. The only PRs that target `master` are the release PR shipping `dev` and a `master`-cut hotfix (see *The two hotfix paths*).

> Use the `ci-gate` skill to satisfy the pipeline and E2E requirements above.
