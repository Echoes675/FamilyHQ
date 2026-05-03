# Master-Chain CI/CD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-promote each successful master build through staging → preprod → prod, threading the master build's SemVer tag through the chain so every link deploys the exact same images, while preserving the dev path and ensuring manual runs of any deploy pipeline never cascade.

**Architecture:** Replace the implicit `upstream(...)` triggers on the master path with explicit `build job: '...', parameters: [...], wait: false, propagate: false` calls in each upstream pipeline's `post { success }` block. Each downstream gains a `SEMVER_TAG` parameter that, when present, short-circuits the existing `Resolve Image` stage to deploy that exact tag. Each chain link fires only when both an `UpstreamCause` and a non-empty `SEMVER_TAG` are present — this conjunction is what enforces "manual runs do not chain" and "dev-triggered staging does not chain to preprod."

**Tech Stack:** Jenkins declarative pipelines, Groovy, Docker registry (`registry.alphaepsilon.co.uk`), `jk` Jenkins CLI for verification.

**Spec:** `docs/superpowers/specs/2026-05-02-master-chain-cicd-design.md`

---

## File Structure

| File | Change kind | Responsibility |
|---|---|---|
| `Jenkinsfile.build` | Modify (`post { success }` only) | On master-branch success, kick off `FamilyHQ-Deploy-Staging` with `BRANCH=master, SEMVER_TAG=$SEMVER_TAG`. |
| `Jenkinsfile.deploy-staging` | Modify (triggers, parameters, Resolve Image, post) | Drop master from upstream-trigger list. Accept `SEMVER_TAG`. Short-circuit Resolve Image when SEMVER_TAG provided. Chain to preprod on success when UpstreamCause + SEMVER_TAG. |
| `Jenkinsfile.deploy-preprod` | Modify (remove triggers, parameters, Resolve Image, post) | Remove existing upstream trigger. Accept `SEMVER_TAG`. Same Resolve Image short-circuit. Chain to `FamilyHQ-Deploy-Production` on success. |
| `Jenkinsfile.deploy-prod` | Untouched | Existing `DIRECTION=specific` + `SEMVER_TAG` surface is reused as-is. |
| `.agent/docs/ci-cd.md` | Modify | Update pipeline trigger table; add "Master release chain" section. |

The four Jenkinsfiles all live at the repo root. The chain order in tasks below mirrors the topological order — staging changes first (it's the chain's first downstream), then preprod, then build, then docs. Build is done last because its only change is `post { success }` and that change is purely additive.

---

## Task 1: Add SEMVER_TAG short-circuit to staging Resolve Image

**Files:**
- Modify: `Jenkinsfile.deploy-staging` (parameters block at lines 16-18; Resolve Image stage at lines 31-72)

This is the lowest-risk change to land first: it only adds a new optional parameter and a new code path. With `SEMVER_TAG=''` (the default), nothing about the existing pipeline changes. Land this on its own so a manual run with `SEMVER_TAG=v1.0.X` can be tested in isolation before any chaining is wired up.

- [ ] **Step 1: Add SEMVER_TAG parameter**

In `Jenkinsfile.deploy-staging`, replace the `parameters { ... }` block:

```groovy
parameters {
    string(name: 'BRANCH', defaultValue: 'dev', description: 'Branch name to deploy (default: dev). Ignored when SEMVER_TAG is set.')
    string(name: 'SEMVER_TAG', defaultValue: '', description: 'When set (master release chain), deploy this SemVer tag verbatim (e.g. v1.0.42) and skip branch-tag resolution.')
}
```

- [ ] **Step 2: Add SEMVER_TAG short-circuit at the top of Resolve Image**

In `Jenkinsfile.deploy-staging`, modify the `Resolve Image` stage. Insert this block at the very top of the `script { }` body, **before** the existing `def branch = params.BRANCH?.trim() ?: 'dev'` line:

```groovy
def semverTag = params.SEMVER_TAG?.trim()
if (semverTag) {
    if (!(semverTag ==~ /^v\d+\.\d+\.\d+$/)) {
        error "SEMVER_TAG must look like v{MAJOR}.{MINOR}.{PATCH} (e.g. v1.0.42). Got: ${semverTag}"
    }
    def tagsJsonForSemver = ''
    withCredentials([usernameColonPassword(credentialsId: 'alphaepsilon-docker-registry', variable: 'REGISTRY_CREDS')]) {
        tagsJsonForSemver = sh(
            script: "curl -sk -u \$REGISTRY_CREDS ${REGISTRY_URL}/v2/familyhq-webapi/tags/list",
            returnStdout: true
        ).trim()
    }
    def rawTagsForSemver = readJSON(text: tagsJsonForSemver).tags
    def tagsForSemver = (rawTagsForSemver != null && rawTagsForSemver instanceof List) ? rawTagsForSemver : []
    if (!tagsForSemver.contains(semverTag)) {
        error "SemVer tag ${semverTag} not found in registry. Available SemVer tags: ${tagsForSemver.findAll { it ==~ /^v\d+\.\d+\.\d+$/ }}"
    }
    env.RESOLVED_IMAGE_TAG = semverTag
    echo "Resolved image tag from SEMVER_TAG: ${env.RESOLVED_IMAGE_TAG}"
    return
}
```

The trailing `return` exits the `script {}` closure and skips the existing branch-tag resolution. The local variable names are suffixed `ForSemver` so they cannot collide with the existing block's variables.

- [ ] **Step 3: Lint the Jenkinsfile locally**

Run: `git diff Jenkinsfile.deploy-staging | head -80`
Expected: only the `parameters` block and the inserted `if (semverTag)` block appear in the diff. The existing branch-resolution code is untouched.

- [ ] **Step 4: Commit**

```bash
git add Jenkinsfile.deploy-staging
git commit -m "feat(cicd): accept SEMVER_TAG in deploy-staging for chained release deploys"
```

- [ ] **Step 5: Push and run CI gate**

Read `.agent/skills/ci-gate/SKILL.md` and follow it. The branch is `feature/master-chain-cicd`. We do NOT raise the PR yet; we are just pushing to verify the build pipeline still passes (it should — only Jenkinsfiles changed, and the staging file has not changed any of its existing logic).

- [ ] **Step 6: Verify the new path manually via Jenkins**

Run via `jk`:
```bash
jk run start FamilyHQ-Deploy-Staging -p BRANCH=master -p SEMVER_TAG=<latest-existing-master-vX.Y.Z> --follow
```
Expected: pipeline logs show `Resolved image tag from SEMVER_TAG: vX.Y.Z`, deploy succeeds, E2E suite runs against staging using that exact image. Confirms the new code path works before any chain wiring depends on it.

If no `vX.Y.Z` master tag exists yet in the registry (because no master build has produced one since the SemVer feature was added), instead run:
```bash
jk run start FamilyHQ-Deploy-Staging -p BRANCH=dev --follow
```
Expected: empty SEMVER_TAG → falls through to existing dev branch-tag resolution, identical to current behaviour. This proves the no-SEMVER-TAG path is unchanged.

---

## Task 2: Add chain trigger to staging post.success

**Files:**
- Modify: `Jenkinsfile.deploy-staging` (post block at lines 229-236)

- [ ] **Step 1: Replace the post.success block**

In `Jenkinsfile.deploy-staging`, replace this block:

```groovy
post {
    success {
        echo "Staging deployment successful: ${env.RESOLVED_IMAGE_TAG}"
    }
    failure {
        echo "Staging deployment failed: ${env.RESOLVED_IMAGE_TAG}"
    }
}
```

with:

```groovy
post {
    success {
        echo "Staging deployment successful: ${env.RESOLVED_IMAGE_TAG}"
        script {
            // Chain to preprod only when this run was triggered from upstream
            // (i.e. by Jenkinsfile.build's master post.success) AND a SemVer
            // tag was threaded through. Manual runs and dev-triggered runs
            // never chain.
            def upstreamCauses = currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause')
            def semverTag = params.SEMVER_TAG?.trim()
            if (upstreamCauses && semverTag) {
                echo "Chaining to FamilyHQ-Deploy-PreProd with SEMVER_TAG=${semverTag}"
                build job: 'FamilyHQ-Deploy-PreProd',
                      parameters: [
                          string(name: 'BRANCH', value: 'master'),
                          string(name: 'SEMVER_TAG', value: semverTag)
                      ],
                      wait: false,
                      propagate: false
            } else {
                echo "Not chaining downstream (upstreamCause=${upstreamCauses ? 'yes' : 'no'}, semverTag=${semverTag ? 'yes' : 'no'})"
            }
        }
    }
    failure {
        echo "Staging deployment failed: ${env.RESOLVED_IMAGE_TAG}"
    }
}
```

- [ ] **Step 2: Lint the diff**

Run: `git diff Jenkinsfile.deploy-staging | tail -60`
Expected: only the `post.success` block changed; `post.failure` untouched.

- [ ] **Step 3: Commit**

```bash
git add Jenkinsfile.deploy-staging
git commit -m "feat(cicd): chain deploy-staging to deploy-preprod on master release runs"
```

Do NOT push yet — preprod is not ready to receive the chain. Push happens after Task 3 lands so the chain has a destination.

---

## Task 3: Drop master from staging upstream triggers

**Files:**
- Modify: `Jenkinsfile.deploy-staging` (triggers block at lines 8-10)

- [ ] **Step 1: Update the upstream trigger list**

In `Jenkinsfile.deploy-staging`, replace:

```groovy
triggers {
    upstream(upstreamProjects: 'FamilyHQ/dev, FamilyHQ/master', threshold: hudson.model.Result.SUCCESS)
}
```

with:

```groovy
triggers {
    // Only dev triggers staging via this implicit channel. The master path is
    // now driven by an explicit `build job:` call from Jenkinsfile.build's
    // post.success so the SemVer tag can be passed through as a parameter.
    upstream(upstreamProjects: 'FamilyHQ/dev', threshold: hudson.model.Result.SUCCESS)
}
```

- [ ] **Step 2: Commit**

```bash
git add Jenkinsfile.deploy-staging
git commit -m "refactor(cicd): drop master from deploy-staging upstream triggers"
```

Do NOT push yet — the master trigger is now lost from this side, but `Jenkinsfile.build` doesn't yet replace it. Pushing would leave a window where master builds don't deploy to staging. Tasks 4-7 must land together with this one.

---

## Task 4: Add SEMVER_TAG short-circuit to preprod Resolve Image

**Files:**
- Modify: `Jenkinsfile.deploy-preprod` (parameters block at lines 16-18; Resolve Image stage at lines 30-71)

- [ ] **Step 1: Add SEMVER_TAG parameter**

Replace the `parameters { ... }` block in `Jenkinsfile.deploy-preprod`:

```groovy
parameters {
    string(name: 'BRANCH', defaultValue: 'master', description: 'Branch name to deploy (default: master). Ignored when SEMVER_TAG is set.')
    string(name: 'SEMVER_TAG', defaultValue: '', description: 'When set (master release chain), deploy this SemVer tag verbatim (e.g. v1.0.42) and skip branch-tag resolution.')
}
```

- [ ] **Step 2: Add SEMVER_TAG short-circuit at the top of Resolve Image**

Insert at the top of the `Resolve Image` `script { }` body, before `def branch = params.BRANCH?.trim() ?: 'master'`:

```groovy
def semverTag = params.SEMVER_TAG?.trim()
if (semverTag) {
    if (!(semverTag ==~ /^v\d+\.\d+\.\d+$/)) {
        error "SEMVER_TAG must look like v{MAJOR}.{MINOR}.{PATCH} (e.g. v1.0.42). Got: ${semverTag}"
    }
    def tagsJsonForSemver = ''
    withCredentials([usernameColonPassword(credentialsId: 'alphaepsilon-docker-registry', variable: 'REGISTRY_CREDS')]) {
        tagsJsonForSemver = sh(
            script: "curl -sk -u \$REGISTRY_CREDS ${REGISTRY_URL}/v2/familyhq-webapi/tags/list",
            returnStdout: true
        ).trim()
    }
    def rawTagsForSemver = readJSON(text: tagsJsonForSemver).tags
    def tagsForSemver = (rawTagsForSemver != null && rawTagsForSemver instanceof List) ? rawTagsForSemver : []
    if (!tagsForSemver.contains(semverTag)) {
        error "SemVer tag ${semverTag} not found in registry. Available SemVer tags: ${tagsForSemver.findAll { it ==~ /^v\d+\.\d+\.\d+$/ }}"
    }
    env.RESOLVED_IMAGE_TAG = semverTag
    echo "Resolved image tag from SEMVER_TAG: ${env.RESOLVED_IMAGE_TAG}"
    return
}
```

(Same shape as Task 1 Step 2 — repeated verbatim per "no Similar to Task N" rule.)

- [ ] **Step 3: Commit**

```bash
git add Jenkinsfile.deploy-preprod
git commit -m "feat(cicd): accept SEMVER_TAG in deploy-preprod for chained release deploys"
```

---

## Task 5: Add chain trigger to preprod post.success

**Files:**
- Modify: `Jenkinsfile.deploy-preprod` (post block at lines 168-175)

- [ ] **Step 1: Replace the post.success block**

Replace:

```groovy
post {
    success {
        echo "PreProd deployment successful: ${env.RESOLVED_IMAGE_TAG}"
    }
    failure {
        echo "PreProd deployment failed: ${env.RESOLVED_IMAGE_TAG}"
    }
}
```

with:

```groovy
post {
    success {
        echo "PreProd deployment successful: ${env.RESOLVED_IMAGE_TAG}"
        script {
            // Chain to production only when this run was triggered from upstream
            // (i.e. from Jenkinsfile.deploy-staging's post.success on a master
            // release run) AND a SemVer tag was threaded through. Manual runs
            // never chain to prod.
            def upstreamCauses = currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause')
            def semverTag = params.SEMVER_TAG?.trim()
            if (upstreamCauses && semverTag) {
                echo "Chaining to FamilyHQ-Deploy-Production with SEMVER_TAG=${semverTag}"
                build job: 'FamilyHQ-Deploy-Production',
                      parameters: [
                          string(name: 'DIRECTION', value: 'specific'),
                          string(name: 'SEMVER_TAG', value: semverTag)
                      ],
                      wait: false,
                      propagate: false
            } else {
                echo "Not chaining to prod (upstreamCause=${upstreamCauses ? 'yes' : 'no'}, semverTag=${semverTag ? 'yes' : 'no'})"
            }
        }
    }
    failure {
        echo "PreProd deployment failed: ${env.RESOLVED_IMAGE_TAG}"
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Jenkinsfile.deploy-preprod
git commit -m "feat(cicd): chain deploy-preprod to deploy-production on master release runs"
```

---

## Task 6: Remove the upstream trigger block from preprod

**Files:**
- Modify: `Jenkinsfile.deploy-preprod` (triggers block at lines 8-10)

- [ ] **Step 1: Remove the triggers block entirely**

Delete this block from `Jenkinsfile.deploy-preprod`:

```groovy
triggers {
    upstream(upstreamProjects: 'FamilyHQ/master', threshold: hudson.model.Result.SUCCESS)
}
```

The `options { disableConcurrentBuilds() }` block stays. The pipeline now only runs when explicitly invoked (manually or via `build job:` from staging).

- [ ] **Step 2: Verify the block is gone**

Run: `git diff Jenkinsfile.deploy-preprod | head -20`
Expected: a `-` for each of the four removed lines (`triggers {`, the `upstream(...)` line, `}`, and the trailing blank line if any).

- [ ] **Step 3: Commit**

```bash
git add Jenkinsfile.deploy-preprod
git commit -m "refactor(cicd): drop direct master upstream trigger from deploy-preprod"
```

Do NOT push yet — same reasoning as Task 3. The chain start in Task 7 must land before pushing.

---

## Task 7: Wire master build to start the chain

**Files:**
- Modify: `Jenkinsfile.build` (post block at lines 251-268)

This is the chain's entry point. Once this lands, master builds resume triggering staging — but now via the explicit, parameterised path.

- [ ] **Step 1: Replace the post.success block**

In `Jenkinsfile.build`, replace:

```groovy
post {
    success {
        echo "Build successful: ${IMAGE_TAG}${env.SEMVER_TAG ? ' (' + env.SEMVER_TAG + ')' : ''}"
    }
```

with:

```groovy
post {
    success {
        echo "Build successful: ${IMAGE_TAG}${env.SEMVER_TAG ? ' (' + env.SEMVER_TAG + ')' : ''}"
        script {
            // Master release chain entry point. Only fire on master, and only
            // when SemVer computation succeeded (env.SEMVER_TAG is set). Dev
            // and feature-branch builds rely on existing upstream triggers.
            if (env.BRANCH_NAME == 'master' && env.SEMVER_TAG?.trim()) {
                echo "Starting master release chain: FamilyHQ-Deploy-Staging with SEMVER_TAG=${env.SEMVER_TAG}"
                build job: 'FamilyHQ-Deploy-Staging',
                      parameters: [
                          string(name: 'BRANCH', value: 'master'),
                          string(name: 'SEMVER_TAG', value: env.SEMVER_TAG)
                      ],
                      wait: false,
                      propagate: false
            }
        }
    }
```

The `failure` block (which deletes a stale local SemVer tag on failure) is unchanged.

- [ ] **Step 2: Verify the diff**

Run: `git diff Jenkinsfile.build`
Expected: only the `post.success` block has changed. The `failure` block, all stages, and the helper methods at the bottom are untouched.

- [ ] **Step 3: Commit**

```bash
git add Jenkinsfile.build
git commit -m "feat(cicd): start master release chain from build post.success"
```

- [ ] **Step 4: Push the whole sequence**

```bash
git push -u origin feature/master-chain-cicd
```

This is the first push since the design-doc commit. After this push, the master chain is wired end-to-end. We do NOT raise the PR yet — we run the verification plan in Task 9 first.

---

## Task 8: Update CI/CD documentation

**Files:**
- Modify: `.agent/docs/ci-cd.md`

Per `feedback_keep_agent_docs_updated`, the doc must be updated as the implementation lands.

- [ ] **Step 1: Update the pipeline trigger table**

In `.agent/docs/ci-cd.md`, replace the existing Pipelines table (lines 5-11) with:

```markdown
| Pipeline | File | Trigger | Purpose |
|----------|------|---------|---------|
| Build | `Jenkinsfile.build` | Push on any branch | Run unit tests, build Docker images, push to registry, prune old tags. On master, also computes/pushes a SemVer tag and starts the master release chain. |
| Deploy Dev | `Jenkinsfile.deploy-dev` | Upstream success on `dev` branch build | Resolve latest `dev-*` image and deploy to 192.168.86.23:8200 |
| Deploy Staging | `Jenkinsfile.deploy-staging` | Upstream success on `dev` build (deploys latest `dev-*` image), OR explicit invocation from master build with `SEMVER_TAG` (master release chain — deploys that exact SemVer image and chains to preprod) | Parameterised deploy of either the dev image line or a pinned master release |
| Deploy Preprod | `Jenkinsfile.deploy-preprod` | Explicit invocation only — from master release chain (Deploy-Staging post.success) or manual run | Deploy a master image (or pinned SemVer) to preprod; chains to production on master release runs |
| Deploy Production | `Jenkinsfile.deploy-prod` | Explicit invocation only — from master release chain (Deploy-PreProd post.success) or manual run | Deploy a specific image to prod, with safety checks |
```

- [ ] **Step 2: Add a "Master release chain" section**

Insert this section between the existing "Deploying to Prod" section (line 60) and the "Auto-Reload Mechanism" section (line 70):

```markdown
## Master Release Chain

Each successful master build automatically promotes its images through staging → preprod → production. The chain is driven by explicit `build job:` calls in each upstream pipeline's `post { success }` block, with the SemVer tag (`vX.Y.Z`) threaded through as a parameter so every link deploys the exact same image set. Manual runs of any deploy pipeline never cascade.

### Flow

1. `Jenkinsfile.build` succeeds on master → computes `SEMVER_TAG=vX.Y.Z`, pushes images and the git tag → `post.success` triggers `FamilyHQ-Deploy-Staging` with `BRANCH=master, SEMVER_TAG=vX.Y.Z`.
2. `Jenkinsfile.deploy-staging` resolves `RESOLVED_IMAGE_TAG=vX.Y.Z` (short-circuiting branch-tag resolution), deploys, runs E2E suite. On success, `post.success` triggers `FamilyHQ-Deploy-PreProd` with the same `SEMVER_TAG`.
3. `Jenkinsfile.deploy-preprod` deploys the same SemVer image. On success, `post.success` triggers `FamilyHQ-Deploy-Production` with `DIRECTION=specific, SEMVER_TAG=vX.Y.Z`.
4. `Jenkinsfile.deploy-prod` deploys the SemVer image to prod via its existing `DIRECTION=specific` surface. Browsers connected to prod auto-reload via the version-mismatch banner (see Auto-Reload Mechanism below).

All `build job:` calls use `wait: false, propagate: false`, so each pipeline reports its own status independently — a downstream failure never retroactively fails an upstream that already succeeded.

### Manual runs do not chain

Each `post.success` chain trigger is gated on the conjunction:

```
currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause') is non-empty
AND params.SEMVER_TAG is non-empty
```

| Trigger source | UpstreamCause? | SEMVER_TAG set? | Chains downstream? |
|---|---|---|---|
| Manual run, no params | no | no | no |
| Manual run, SEMVER_TAG set by hand | no | yes | no |
| Dev build → staging via `upstream` | yes | no | no |
| Master release chain | yes | yes | yes |

### Recovering from a mid-chain failure

If staging or preprod fails on a master release run, the chain stops at the failed link. Two recovery options:

- **Re-run the master build** (cleanest) — the build pipeline computes a fresh `SEMVER_TAG` (next patch number) and re-walks the chain end-to-end.
- **Manually invoke the failed pipeline** with the original `SEMVER_TAG` — this WILL deploy that environment, but will NOT chain further (no `UpstreamCause`). Subsequent environments must be progressed manually too. This is intentional: any human-touched run requires explicit human progression.
```

- [ ] **Step 3: Commit**

```bash
git add .agent/docs/ci-cd.md
git commit -m "docs(cicd): document master release chain and chain-gating rules"
```

- [ ] **Step 4: Push the docs update**

```bash
git push
```

---

## Task 9: End-to-end verification on Jenkins

Per `feedback_jenkins_completion` and `feedback_verify_all_envs`, the work is not complete until the chain has been observed end-to-end on the live Jenkins controller.

- [ ] **Step 1: Confirm dev path is unchanged**

Push a trivial change to `dev` (e.g. via a separate branch and merge, or by pushing this branch to dev after PR review — whichever fits the workflow). Watch:

```bash
jk run ls FamilyHQ/dev --limit 1
jk run ls FamilyHQ-Deploy-Staging --limit 1
jk run ls FamilyHQ-Deploy-PreProd --limit 5
```

Expected:
- The dev build runs and succeeds.
- `FamilyHQ-Deploy-Staging` fires once (upstream-triggered with empty `SEMVER_TAG`, `BRANCH=dev`), deploys the latest `dev-*` image.
- `FamilyHQ-Deploy-PreProd` does NOT fire (no recent run after the staging completion). Tail the staging build log via `jk log FamilyHQ-Deploy-Staging <num>` and confirm it printed `Not chaining downstream (upstreamCause=yes, semverTag=no)`.

- [ ] **Step 2: Confirm manual staging run does not chain**

```bash
jk run start FamilyHQ-Deploy-Staging -p BRANCH=dev --follow
```

Expected:
- Staging deploys.
- Log prints `Not chaining downstream (upstreamCause=no, semverTag=no)`.
- `FamilyHQ-Deploy-PreProd` is not queued.

- [ ] **Step 3: Confirm manual staging run with SEMVER_TAG set by hand does not chain**

Find an existing `vX.Y.Z` master tag in the registry (or skip this step if none exists yet — Step 4 will produce one):

```bash
jk run start FamilyHQ-Deploy-Staging -p BRANCH=master -p SEMVER_TAG=vX.Y.Z --follow
```

Expected:
- Staging resolves the SemVer image and deploys.
- Log prints `Not chaining downstream (upstreamCause=no, semverTag=yes)`.
- `FamilyHQ-Deploy-PreProd` is not queued.

- [ ] **Step 4: Confirm full master chain end-to-end**

Merge `feature/master-chain-cicd` into master via PR (after Step 1 has confirmed dev is fine, the PR has been reviewed, and merge to dev → master via the project's normal branching flow has happened). Then:

```bash
jk run ls FamilyHQ/master --limit 1
jk run ls FamilyHQ-Deploy-Staging --limit 1
jk run ls FamilyHQ-Deploy-PreProd --limit 1
jk run ls FamilyHQ-Deploy-Production --limit 1
```

Expected sequence (each must SUCCESS before the next is queued):
1. `FamilyHQ/master #N` — SUCCESS, log shows `Starting master release chain: FamilyHQ-Deploy-Staging with SEMVER_TAG=vX.Y.Z`.
2. `FamilyHQ-Deploy-Staging #M` — SUCCESS, started by upstream cause from `FamilyHQ/master #N`, deploys `:vX.Y.Z`, log shows `Chaining to FamilyHQ-Deploy-PreProd with SEMVER_TAG=vX.Y.Z`.
3. `FamilyHQ-Deploy-PreProd #P` — SUCCESS, started by upstream cause from `FamilyHQ-Deploy-Staging #M`, deploys `:vX.Y.Z`, log shows `Chaining to FamilyHQ-Deploy-Production with SEMVER_TAG=vX.Y.Z`.
4. `FamilyHQ-Deploy-Production #Q` — SUCCESS, started by upstream cause from `FamilyHQ-Deploy-PreProd #P`, deploys `:vX.Y.Z` to prod.

Then in a browser:
- Hit `https://familyhq.api.alphaepsilon.co.uk/api/health`. Confirm `version` reports `vX.Y.Z`.
- Open the dashboard at `https://familyhq.alphaepsilon.co.uk`. Confirm footer reads `vX.Y.Z`.

- [ ] **Step 5: Confirm cause threading via `jk run view`**

For each chained run:

```bash
jk run view FamilyHQ-Deploy-Staging <M> --json --jq '.causes'
jk run view FamilyHQ-Deploy-PreProd <P> --json --jq '.causes'
jk run view FamilyHQ-Deploy-Production <Q> --json --jq '.causes'
```

Expected: each shows an `UpstreamCause` referencing the previous link. (If `--jq` syntax differs in this version of `jk`, use `--json` alone and grep the result.)

---

## Task 10: Open PR

Once all of Task 9 is green:

- [ ] **Step 1: Open PR from `feature/master-chain-cicd` into `dev`**

Per `.agent/skills/git-workflow/SKILL.md`, follow the project's PR conventions. Title: `feat(cicd): chain master builds through staging → preprod → prod with SemVer threading`.

PR description should reference:
- The design doc: `docs/superpowers/specs/2026-05-02-master-chain-cicd-design.md`
- The plan: `docs/superpowers/plans/2026-05-02-master-chain-cicd.md`
- Verification evidence from Task 9 (Jenkins run numbers, screenshots of `/api/health` showing the new version).

- [ ] **Step 2: Request review**

Per `feedback_superpowers_workflow`, also run the `superpowers:requesting-code-review` skill against the changes before merging.

---

## Self-review notes

**Spec coverage check:**

| Spec section | Plan task |
|---|---|
| Architecture: chain topology | Tasks 7 (build), 2 (staging→preprod), 5 (preprod→prod) |
| Architecture: triggers to remove | Tasks 3 (staging), 6 (preprod) |
| Per-file: `Jenkinsfile.build` post.success | Task 7 |
| Per-file: `Jenkinsfile.deploy-staging` parameters + Resolve Image | Task 1 |
| Per-file: `Jenkinsfile.deploy-staging` triggers | Task 3 |
| Per-file: `Jenkinsfile.deploy-staging` post.success | Task 2 |
| Per-file: `Jenkinsfile.deploy-preprod` parameters + Resolve Image | Task 4 |
| Per-file: `Jenkinsfile.deploy-preprod` post.success | Task 5 |
| Per-file: `Jenkinsfile.deploy-preprod` remove triggers | Task 6 |
| Per-file: `Jenkinsfile.deploy-prod` no changes | (intentionally no task) |
| Gating logic | Tasks 2, 5 (the conjunction) |
| Documentation updates | Task 8 |
| Verification plan | Task 9 |

All spec sections covered. No placeholders. Type / parameter / job-name consistency verified across tasks (BRANCH, SEMVER_TAG, DIRECTION, RESOLVED_IMAGE_TAG, FamilyHQ-Deploy-Staging, FamilyHQ-Deploy-PreProd, FamilyHQ-Deploy-Production all match between tasks and design doc).
