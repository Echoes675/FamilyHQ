# Master-Chain CI/CD Design

## Goal

Promote a master build through staging → preprod → prod automatically, while preserving the existing dev path and ensuring manual runs of any deploy pipeline never cascade downstream.

## Constraints

1. A successful master build must auto-deploy to staging, then on staging success to preprod, then on preprod success to prod.
2. Each chain link must be reproducible: the exact image produced by the master build is the same image deployed to staging, preprod, and prod.
3. Manual runs of `FamilyHQ-Deploy-Staging`, `FamilyHQ-Deploy-PreProd`, or `FamilyHQ-Deploy-Production` must NOT trigger the next pipeline.
4. The dev path (`FamilyHQ/dev` build → `FamilyHQ-Deploy-Dev` and `FamilyHQ-Deploy-Staging` with `BRANCH=dev`) must remain unchanged.
5. Each pipeline reports its own status; a downstream failure must not retroactively fail an upstream that already succeeded.

## Architecture

### Chain topology

```
FamilyHQ/<branch> (build)
  ├─ branch == dev    → FamilyHQ-Deploy-Dev (existing upstream trigger, unchanged)
  │                     FamilyHQ-Deploy-Staging(BRANCH=dev) (existing upstream trigger, unchanged)
  └─ branch == master → FamilyHQ-Deploy-Staging(BRANCH=master, SEMVER_TAG=vX.Y.Z) [new explicit build job: call]
                          └─ post.success + UpstreamCause + SEMVER_TAG present:
                              → FamilyHQ-Deploy-PreProd(BRANCH=master, SEMVER_TAG=vX.Y.Z) [new explicit build job: call]
                                  └─ post.success + UpstreamCause + SEMVER_TAG present:
                                      → FamilyHQ-Deploy-Production(DIRECTION=specific, SEMVER_TAG=vX.Y.Z) [new explicit build job: call]
```

Two existing trigger blocks must be removed:

- `FamilyHQ/master` is removed from the `upstream(...)` list in `Jenkinsfile.deploy-staging` (only `FamilyHQ/dev` remains).
- The whole `triggers { upstream('FamilyHQ/master') }` block in `Jenkinsfile.deploy-preprod` is removed.

The master path is now driven entirely by explicit `build job:` calls so parameters (`SEMVER_TAG`) can be threaded through. The dev path stays on the `upstream(...)` mechanism since it requires no parameter passing.

### Why explicit calls and not upstream triggers for the master chain

Jenkins `upstream` triggers do not pass parameters to downstream jobs. Threading the `SEMVER_TAG` through the chain requires `build job: '...', parameters: [...]`, which is only available via explicit invocation in `post { success }`.

### Why `wait: false, propagate: false`

- `wait: false` — the upstream's pipeline result is determined by its own stages, not the downstream's runtime.
- `propagate: false` — a downstream FAILURE does not cause the upstream to be marked failed retroactively.

This satisfies constraint 5: each environment owns its own status.

## Per-file changes

### `Jenkinsfile.build`

Add to `post { success }`:

```groovy
script {
    if (env.BRANCH_NAME == 'master' && env.SEMVER_TAG?.trim()) {
        build job: 'FamilyHQ-Deploy-Staging',
              parameters: [
                  string(name: 'BRANCH', value: 'master'),
                  string(name: 'SEMVER_TAG', value: env.SEMVER_TAG)
              ],
              wait: false,
              propagate: false
    }
}
```

The `env.SEMVER_TAG` guard is defensive: if SemVer computation failed for any reason, no chain fires. The `BRANCH_NAME == 'master'` guard ensures dev / feature-branch builds don't accidentally invoke staging via this path (dev still goes via the existing upstream trigger).

### `Jenkinsfile.deploy-staging`

1. **Trigger update:** drop `FamilyHQ/master` from the `upstream(...)` projects list:

   ```groovy
   triggers {
       upstream(upstreamProjects: 'FamilyHQ/dev', threshold: hudson.model.Result.SUCCESS)
   }
   ```

2. **Add SEMVER_TAG parameter:**

   ```groovy
   parameters {
       string(name: 'BRANCH', defaultValue: 'dev', description: 'Branch name to deploy (default: dev)')
       string(name: 'SEMVER_TAG', defaultValue: '', description: 'When set (master chain), deploy this SemVer tag verbatim and bypass branch-tag resolution.')
   }
   ```

3. **Resolve Image stage** — short-circuit when SEMVER_TAG is provided:

   ```groovy
   stage('Resolve Image') {
       steps {
           echo '======= Resolve Image ======='
           script {
               def semverTag = params.SEMVER_TAG?.trim()
               if (semverTag) {
                   if (!(semverTag ==~ /^v\d+\.\d+\.\d+$/)) {
                       error "SEMVER_TAG must look like v{MAJOR}.{MINOR}.{PATCH}. Got: ${semverTag}"
                   }
                   // Validate it exists in the registry
                   def tagsJson = ''
                   withCredentials([usernameColonPassword(credentialsId: 'alphaepsilon-docker-registry', variable: 'REGISTRY_CREDS')]) {
                       tagsJson = sh(
                           script: "curl -sk -u \$REGISTRY_CREDS ${REGISTRY_URL}/v2/familyhq-webapi/tags/list",
                           returnStdout: true
                       ).trim()
                   }
                   def rawTags = readJSON(text: tagsJson).tags
                   def tags = (rawTags != null && rawTags instanceof List) ? rawTags : []
                   if (!tags.contains(semverTag)) {
                       error "SemVer tag ${semverTag} not found in registry."
                   }
                   env.RESOLVED_IMAGE_TAG = semverTag
                   echo "Resolved image tag from SEMVER_TAG: ${env.RESOLVED_IMAGE_TAG}"
                   return
               }
               // Existing branch-based resolution path is unchanged below…
               // (BRANCH parameter, build-number sort, etc.)
           }
       }
   }
   ```

4. **Chain to preprod** — add to `post { success }`:

   ```groovy
   script {
       def upstreamCauses = currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause')
       def semverTag = params.SEMVER_TAG?.trim()
       if (upstreamCauses && semverTag) {
           build job: 'FamilyHQ-Deploy-PreProd',
                 parameters: [
                     string(name: 'BRANCH', value: 'master'),
                     string(name: 'SEMVER_TAG', value: semverTag)
                 ],
                 wait: false,
                 propagate: false
       }
   }
   ```

### `Jenkinsfile.deploy-preprod`

1. **Remove `triggers { upstream(...) }` block entirely.** (Master chain now arrives via explicit `build job:` from staging; manual runs are still supported.)

2. **Add SEMVER_TAG parameter** (same shape as staging).

3. **Resolve Image stage** — same SEMVER_TAG short-circuit pattern as staging.

4. **Chain to prod** — add to `post { success }`:

   ```groovy
   script {
       def upstreamCauses = currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause')
       def semverTag = params.SEMVER_TAG?.trim()
       if (upstreamCauses && semverTag) {
           build job: 'FamilyHQ-Deploy-Production',
                 parameters: [
                     string(name: 'DIRECTION', value: 'specific'),
                     string(name: 'SEMVER_TAG', value: semverTag)
                 ],
                 wait: false,
                 propagate: false
       }
   }
   ```

### `Jenkinsfile.deploy-prod`

**No changes.** The pipeline already accepts `DIRECTION=specific` + `SEMVER_TAG` and validates both. The auto-trigger from preprod is just a normal invocation of the existing surface.

## Gating logic

The chain-firing condition at each link is the conjunction:

```
currentBuild.getBuildCauses('hudson.model.Cause$UpstreamCause') is non-empty
AND params.SEMVER_TAG is non-empty
```

Behaviour table:

| Trigger source | UpstreamCause? | SEMVER_TAG set? | Chains downstream? |
|---|---|---|---|
| Manual run, no params | no | no | **no** ✅ |
| Manual run, SEMVER_TAG set by hand | no | yes | **no** (no upstream cause) ✅ |
| Dev build → staging via `upstream` trigger | yes | no | **no** (no SEMVER_TAG) ✅ |
| Master build → staging via explicit `build job:` | yes | yes | **yes** ✅ |
| Staging → preprod via explicit `build job:` | yes | yes | **yes** ✅ |
| Preprod → prod via explicit `build job:` | yes | yes | **yes** ✅ |

The "AND" is important: dropping either condition admits an unwanted case. For example, requiring only `UpstreamCause` would still chain dev→staging→preprod (wrong); requiring only `SEMVER_TAG` would still chain a manual run with the param set (violates the user's stated constraint).

## Edge cases

1. **Master build computes SEMVER_TAG but image push or git tag push fails before the chain fires.**  Handled by existing `Jenkinsfile.build` `post { failure }` — chain is in `post.success` and never runs on failure.

2. **Two master commits land in quick succession.** Each master build computes its own `SEMVER_TAG` (e.g. v1.0.42, v1.0.43) and queues its own chain. Because every pipeline has `disableConcurrentBuilds()`, the second chain's staging run waits for the first to finish. Each release walks the chain in order; no interleaving.

3. **Staging fails on a master-chain run.** Chain stops at staging. Master build's status is unaffected (separate pipeline). Operator inspects staging, fixes, retries. Retry options:
   - Re-run the master build (cleanest — produces a fresh `SEMVER_TAG` and re-walks the chain).
   - Manually run `FamilyHQ-Deploy-Staging` with `SEMVER_TAG=v1.0.42` — this WILL deploy to staging but will NOT chain to preprod (no UpstreamCause). Operator must then manually trigger preprod, which likewise will NOT chain to prod. This is intentional: any human-touched run requires explicit human progression.

4. **Preprod fails on a master-chain run.** Same shape as case 3. Manual progression to prod is allowed via `FamilyHQ-Deploy-Production` with `DIRECTION=specific, SEMVER_TAG=v1.0.42`.

5. **Operator manually triggers a deploy with no SEMVER_TAG (BRANCH-only).** Existing branch-tag resolution path runs (unchanged). No chaining. This preserves the current `BRANCH=dev` workflow.

6. **Operator manually triggers Deploy-Production with `DIRECTION=specific` and a SemVer tag from an old release (rollback).** Works exactly as today. Prod has no downstream so no chain consideration.

7. **A master build is retried after a chain partially completed.** The build pipeline's existing SemVer logic increments the patch number (computeNextPatch reads `git tag -l`), so a re-run produces a new tag. Each chain run is keyed to its own SEMVER_TAG; no two chains ever try to deploy the same tag, so there is no idempotency conflict.

8. **Jenkins controller restart mid-chain.** `wait: false` means each `build job:` call queues the downstream and immediately returns. Once queued, downstream survives controller restart per Jenkins durability semantics. The upstream's `post.success` block has already run, so no orphan state.

## Documentation updates

`.agent/docs/ci-cd.md` is the canonical reference and must be updated alongside the Jenkinsfile changes (per `feedback_keep_agent_docs_updated`):

1. **Pipeline table** — change the Trigger column for staging, preprod, and prod:
   - Deploy Staging: `Upstream success on dev build, OR explicit invocation from master build with SEMVER_TAG (master chain)`
   - Deploy Preprod: `Explicit invocation from staging with SEMVER_TAG (master chain only)`
   - Deploy Prod: `Explicit invocation from preprod with DIRECTION=specific + SEMVER_TAG (master chain), OR manual`

2. **Add a "Master release chain" section** documenting:
   - The four-stage pipeline order.
   - The SEMVER_TAG threading.
   - The "manual runs do not chain" property and how to retry after a mid-chain failure.

## Verification plan

Before raising a PR for review (per `feedback_verify_all_envs`):

### Pre-merge (on `feature/master-chain-cicd`)

1. **Manual Deploy-Staging run with no SEMVER_TAG (BRANCH=dev):** dev images deployed; preprod NOT triggered. (Confirms dev path unchanged.)
2. **Manual Deploy-Staging run with no SEMVER_TAG (BRANCH=master):** latest master-* image deployed; preprod NOT triggered. (Confirms branch-tag fallback works.)
3. **Manual Deploy-Staging run with SEMVER_TAG set by hand:** that SemVer image deployed; preprod NOT triggered (no UpstreamCause).
4. **Manual Deploy-PreProd run:** preprod deploys; prod NOT triggered.
5. **Push to dev → Deploy-Staging fires via upstream → Deploy-PreProd NOT triggered.** (Dev path stays unchained.)

### Master chain end-to-end

6. **Push to master:**
   - Build runs, computes `SEMVER_TAG=v1.0.X`, pushes images and git tag.
   - On build success, Deploy-Staging is queued with `BRANCH=master, SEMVER_TAG=v1.0.X`.
   - Deploy-Staging deploys `:v1.0.X` images, runs E2E suite, on success queues Deploy-PreProd with `SEMVER_TAG=v1.0.X`.
   - Deploy-PreProd deploys `:v1.0.X`, on success queues Deploy-Production with `DIRECTION=specific, SEMVER_TAG=v1.0.X`.
   - Deploy-Production deploys `:v1.0.X`, `/api/health` reports the new version, footer reads `vX.Y.Z`, auto-reload banner appears in any open browser.

### Failure-isolation check

7. Inject a forced failure into preprod (e.g. temporarily make a wait-for-services endpoint unreachable). Confirm:
   - Staging stays SUCCESS for that run.
   - Master build stays SUCCESS for that run.
   - Prod is NOT triggered.
   - Fix the failure, re-run preprod manually with `SEMVER_TAG=v1.0.X`. Confirm prod is still NOT triggered (manual run, no chain).

## Files to be modified

- `Jenkinsfile.build` — add post.success chain trigger.
- `Jenkinsfile.deploy-staging` — drop master from upstream triggers, add SEMVER_TAG parameter, add SEMVER_TAG short-circuit in Resolve Image, add post.success chain trigger.
- `Jenkinsfile.deploy-preprod` — remove upstream trigger block, add SEMVER_TAG parameter, add SEMVER_TAG short-circuit in Resolve Image, add post.success chain trigger.
- `.agent/docs/ci-cd.md` — update pipeline table and add master-chain section.

## Out of scope

- Approval gates / blackout windows for prod (explicitly chosen as fully automatic per requirement).
- Changes to `Jenkinsfile.deploy-prod` itself (its existing `DIRECTION=specific` surface is reused as-is).
- Changes to `Jenkinsfile.deploy-dev` (dev path is untouched).
- Notifications / Slack / email on chain progression (not requested).
