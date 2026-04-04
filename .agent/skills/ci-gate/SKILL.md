---
name: ci-gate
description: Drives a feature branch through the full CI verification loop — push to remote, monitor the Jenkins branch build, trigger FamilyHQ-Deploy-Dev, and confirm passing E2E runs. Use this whenever local unit tests have passed and work is ready to verify. Two modes: (1) mid-work checkpoint between tasks in a larger piece of work — 1 passing run is sufficient; (2) pre-PR gate when the branch is ready to merge — requires 3 consecutive passing runs. Also use when the user says they're "done", "finished", "ready to push", or "ready for PR".
---

# CI Gate

This skill verifies a branch is in a good state via Jenkins. It has two modes:

| Mode | When to use | Passing runs required |
|------|-------------|----------------------|
| **Mid-work checkpoint** | Between tasks in a larger piece of work, to verify each stage before continuing | 1 |
| **Pre-PR gate** | Branch is complete and ready to raise a PR | 3 consecutive |

If in doubt, use **pre-PR** (3 runs).

## Before starting

Confirm:
- Local unit tests have been run and all pass
- `jk` is available: `jk --version` (if missing, see the `jk` skill for install instructions)
- You are on a feature or fix branch — never on `dev` or `master`

Track a **consecutive pass counter** (starts at 0) throughout this process. It resets to 0 every time you push new code.

---

## Step 1 — Push to remote

```bash
git push origin <branch-name>
```

Note the branch name — you'll use it throughout.

---

## Step 2 — Trigger a Jenkins scan and monitor the branch build

There is no GitHub webhook configured, so Jenkins will not pick up the push automatically. Trigger a multibranch pipeline scan first:

```bash
jk run start FamilyHQ
```

This tells Jenkins to scan for new/updated branches. Wait a moment for the scan to complete, then check whether a build has been queued for your branch:

```bash
jk run ls FamilyHQ/<branch-name> --limit 1 --include-queued
```

If the branch job does not appear immediately, wait a few seconds and retry — the scan may still be in progress.

Once a run appears, follow its logs:

```bash
jk log FamilyHQ/<branch-name> <run-number> --follow
```

Wait for the build to reach a terminal state. Do **not** proceed to Step 3 until the result is `SUCCESS`.

### If the build fails

- Read the log carefully to identify the root cause
- If the failure is caused by your changes: fix locally, run unit tests, push, then return to **Step 2** (trigger the scan again and reset the pass counter)
- If the failure appears unrelated: don't assume flakiness — check recent build history (`jk run ls FamilyHQ/<branch-name> --limit 5`) to see if it's a consistent issue before investigating further

---

## Step 3 — Trigger FamilyHQ-Deploy-Dev

```bash
jk run start FamilyHQ-Deploy-Dev -p BRANCH=<branch-name> --follow
```

This deploys to the dev environment and runs all E2E tests.

Note the run number when it starts.

---

## Step 4 — Assess E2E results

Once the pipeline completes, pull the test report:

```bash
jk test report FamilyHQ-Deploy-Dev <run-number> --json
```

### All tests pass

Increment the consecutive pass counter.

- **Mid-work checkpoint**: 1 pass is sufficient — proceed to Step 5
- **Pre-PR gate**: continue triggering (Step 3) until the counter reaches **3 consecutive passes**, then proceed to Step 5

### Tests fail — caused by your changes

- Investigate the failure in the pipeline output and test report
- Fix the code locally, run relevant unit tests to confirm
- Push and return to **Step 2** — the build must pass before re-triggering Deploy-Dev
- Reset the consecutive pass counter to 0

### Tests fail — appears unrelated to your changes

Do not dismiss these. Investigate first:

- Is this a race condition or timing issue? Look for inconsistent waits, missing explicit locator waits, or test ordering sensitivity
- Is this an isolation issue? Check whether the test depends on state left by another scenario
- Run the pipeline again (Step 3) to confirm whether the failure is intermittent or consistent
- If genuinely flaky: mitigate it — add explicit waits, fix isolation, guard the race condition. The goal is a reliable suite, not a green number
- If the failure is environmental and beyond your control (e.g., external dependency outage): document the finding and flag it to the user before continuing

Push any fixes, return to **Step 2**, reset the consecutive pass counter to 0.

---

## Step 5 — Branch is CI-verified

3 consecutive passing runs of `FamilyHQ-Deploy-Dev` have been confirmed.

Report back to the user:
- Branch name and the 3 passing run numbers
- Any flakiness investigations performed and what was done
- Any follow-up items surfaced during the process

The branch is now ready for a PR targeting `dev`.
