# CI/CD: Build, Versioning, and Deploy Pipelines

## Pipelines

| Pipeline | File | Trigger | Purpose |
|----------|------|---------|---------|
| Build | `Jenkinsfile.build` | Push on any branch | Run unit tests, build Docker images, push to registry, prune old tags. On `master`, also computes/pushes a SemVer tag and starts the master release chain. |
| Deploy Dev | `Jenkinsfile.deploy-dev` | Upstream success on `dev` branch build | Resolve latest `dev-*` image and deploy to 192.168.86.23:8200 |
| Deploy Staging | `Jenkinsfile.deploy-staging` | Upstream success on `dev` build (deploys latest `dev-*` image), OR explicit invocation from master build with `SEMVER_TAG` (master release chain — deploys that exact SemVer image and chains to preprod) | Parameterised deploy of either the dev image line or a pinned master release |
| Deploy Preprod | `Jenkinsfile.deploy-preprod` | Explicit invocation only — from master release chain (Deploy-Staging post.success) or manual run | Deploy a master image (or pinned SemVer) to preprod; chains to production on master release runs |
| Deploy Production | `Jenkinsfile.deploy-prod` | Explicit invocation only — from master release chain (Deploy-PreProd post.success) or manual run | Deploy a specific image to prod, with safety checks |

## Image Tagging

Three images are produced per build: `familyhq-webapi`, `familyhq-webui`, `familyhq-simulator`. They are pushed to `registry.alphaepsilon.co.uk`.

Per-build tag (every branch): `{branch-sanitized}-{build-number}-{git-short-sha}`
- Example: `dev-142-a1b2c3d`
- Used by deploy-dev, deploy-staging, deploy-preprod for normal upstream-triggered deploys.

On master only:
- `:latest` — moving pointer to the newest master build.
- `v{MAJOR}.{MINOR}.{PATCH}` — durable SemVer tag derived by Jenkins (see Versioning below). Used by deploy-prod when invoked with `DIRECTION=specific`.

## Versioning

Version numbers are SemVer (`MAJOR.MINOR.PATCH`) derived at build time by [MinVer](https://github.com/adamralph/minver).

- **MAJOR / MINOR** — pinned in `Directory.Build.props` via `<MinVerMinimumMajorMinor>`. Edited by humans in normal feature-branch PRs (see `.agent/skills/git-workflow/SKILL.md` for when to bump).
- **PATCH** — auto-incremented by `Jenkinsfile.build` on master only. The pipeline reads `<MinVerMinimumMajorMinor>` from `Directory.Build.props`, finds the highest existing `v{M}.{m}.*` git tag, and computes the next patch number. The first master build after a MINOR bump produces `v{M}.{m}.0`.

### Build-time version flow

`.dockerignore` excludes `.git`, so MinVer cannot read tag history inside the Docker build. The pipeline therefore passes the computed SemVer into the build via `--build-arg VERSION={SEMVER_TAG}`. Each Dockerfile declares `ARG VERSION` and sets `ENV MINVER_VERSION_OVERRIDE=$VERSION` before `dotnet publish`. `Directory.Build.props` honours that override via `<MinVerVersionOverride>$(MINVER_VERSION_OVERRIDE)</MinVerVersionOverride>` — the only path MinVer respects when its own targets run after evaluation.

For local `dotnet build` (where `.git` is present), the override is empty and MinVer derives the version from tag history. Before any tag exists, builds report something like `1.0.0-alpha.0.{height}+{shortSha}`.

### Git tag push

After a successful master build and image push, `Jenkinsfile.build` pushes an annotated tag `v{M}.{m}.{p}` to the master commit. No explicit credential block is required in the Jenkinsfile — `checkout scm` sets up a credential helper for the workspace using whatever credential the multibranch pipeline is configured with for SCM (currently `jenkins_github`). Subsequent `git push origin v{M}.{m}.{p}` inherits that helper transparently. This is the same pattern `Jenkinsfile.cleanup` uses for `git ls-remote --heads origin`.

The pipeline never modifies repository content; only tags are pushed.

### Required Jenkins env vars

Annotated tag creation needs `user.email` and `user.name`. To avoid hard-coding identity in the repo, the Jenkinsfile reads them from controller-level environment variables. Configure both in **Manage Jenkins → System → Global properties → Environment variables** before the first master build:

| Variable | Example |
|----------|---------|
| `FAMILYHQ_GIT_TAG_EMAIL` | `jenkins@familyhq.alphaepsilon.co.uk` |
| `FAMILYHQ_GIT_TAG_NAME`  | `FamilyHQ Jenkins` |

Values are applied per-command via `git -c user.email=… -c user.name=…` (no global git config mutation). If either variable is missing or blank, the master build fails fast at the `Push Git Tag` stage with a clear error pointing back here.

### Failure recovery

- **Local SemVer tag created but registry push failed** — `post { failure }` in `Jenkinsfile.build` deletes the local tag on the agent so the next attempt re-uses the same number. The remote tag is only pushed in the dedicated final stage, so origin never sees a "tag without image" state.
- **Tag pushed but follow-up step failed** — operator intervention: `git push --delete origin v{M}.{m}.{p}` then `git tag -d v{M}.{m}.{p}`. Then re-run the master build.

## Deploying to Prod

`Jenkinsfile.deploy-prod` is **manual** and supports three `DIRECTION` modes:

- `forward` (default) — picks the newest `master-*` build tag.
- `backward` — rolls back one master image revision.
- `specific` — requires a `SEMVER_TAG` parameter (e.g. `v1.2.0`). Validated against the registry's `tags/list` for `familyhq-webapi` before deploy.

For releases use `DIRECTION=specific` with the `vX.Y.Z` tag, so the deploy is reproducible by tag rather than by build number. The master release chain (below) invokes prod automatically using exactly this surface.

## Master Release Chain

Each successful master build automatically promotes its images through staging → preprod → production. The chain is driven by explicit `build job:` calls in each upstream pipeline's `post { success }` block, with the SemVer tag (`vX.Y.Z`) threaded through as a parameter so every link deploys the exact same image set. Manual runs of any deploy pipeline never cascade.

### Flow

1. `Jenkinsfile.build` succeeds on master → computes `SEMVER_TAG=vX.Y.Z`, pushes images and the git tag → `post.success` triggers `FamilyHQ-Deploy-Staging` with `BRANCH=master, SEMVER_TAG=vX.Y.Z`.
2. `Jenkinsfile.deploy-staging` resolves `RESOLVED_IMAGE_TAG=vX.Y.Z` (short-circuiting branch-tag resolution), deploys, runs the E2E suite. On success, `post.success` triggers `FamilyHQ-Deploy-PreProd` with the same `SEMVER_TAG`.
3. `Jenkinsfile.deploy-preprod` deploys the same SemVer image. On success, `post.success` triggers `FamilyHQ-Deploy-Production` with `DIRECTION=specific, SEMVER_TAG=vX.Y.Z`.
4. `Jenkinsfile.deploy-prod` deploys the SemVer image to prod via its existing `DIRECTION=specific` surface. Browsers connected to prod auto-reload via the version-mismatch banner (see Auto-Reload Mechanism below).

All `build job:` calls use `wait: false, propagate: false`, so each pipeline reports its own status independently — a downstream failure never retroactively fails an upstream that already succeeded.

### Manual runs do not chain

Each `post.success` chain trigger is gated on the conjunction:

```
currentBuild.upstreamBuilds is non-empty
AND params.SEMVER_TAG is non-empty
```

(`upstreamBuilds` is preferred over `getBuildCauses('hudson.model.Cause$UpstreamCause')` because the class-filtered `getBuildCauses` overload returns empty under the current Jenkins/Pipeline version even when the upstream cause is set — first observed on master #27 → staging #84.)

| Trigger source | upstreamBuilds non-empty? | SEMVER_TAG set? | Chains downstream? |
|---|---|---|---|
| Manual run, no params | no | no | no |
| Manual run, SEMVER_TAG set by hand | no | yes | no |
| Dev build → staging via `upstream` | yes | no | no |
| Master release chain | yes | yes | yes |

### Recovering from a mid-chain failure

If staging or preprod fails on a master release run, the chain stops at the failed link. Two recovery options:

- **Re-run the master build** (cleanest) — the build pipeline computes a fresh `SEMVER_TAG` (next patch number) and re-walks the chain end-to-end.
- **Manually invoke the failed pipeline** with the original `SEMVER_TAG` — this WILL deploy that environment, but will NOT chain further (no `UpstreamCause`). Subsequent environments must be progressed manually too. This is intentional: any human-touched run requires explicit human progression.

## Per-environment sync polling interval

`Sync:PeriodicSyncInterval` drives `SyncOrchestrator`'s safety-net timer — how often each environment
enqueues a `Periodic` sync-all. It is **not** the same everywhere, and two of the four values cannot
be found by searching this repository, so this table is the only place they are written down together.

| Environment | Interval | Where it is set |
|---|---|---|
| Code default | 1 hour | `SyncOptions.PeriodicSyncInterval` |
| Dev, Staging | 5 minutes | `appsettings.Development.json` (in this repo) |
| **PreProd** | **10 minutes** | **`Sync__PeriodicSyncInterval` in the `familyhq-preprod-env` file credential** |
| Production | 1 hour | `appsettings.json` (in this repo) |

Two things about this are easy to get wrong:

- **PreProd runs as `ASPNETCORE_ENVIRONMENT=Production`** (`docker-compose.preprod.yml`), so it never
  reads `appsettings.Development.json`. Until the preprod credential set an explicit value, preprod
  therefore inherited production's one-hour interval *by accident* — nobody had decided that preprod
  should poll as slowly as production does.
- **The preprod value lives in a Jenkins file credential, so it is invisible from this repository.**
  That is the established pattern for deployed non-secret configuration here (the `FEATURE_*_ENABLED`
  dev flags work the same way), but it means a search for the interval finds one hour and is wrong.
  Check the credential, or check preprod's Seq logs: `Periodic sync: enqueued sync-all` should appear
  roughly every 10 minutes.

### Why preprod polls faster than production

A defect on the periodic sync path once stopped all calendar syncing in production for five hours. It
was invisible to the post-deploy check because that check ran two minutes after the deploy and the
failing code path does not run until the first periodic sync — an hour later. Sync-affecting changes
are now soaked on preprod across **at least two polling cycles** before release.

At an hourly interval that soak costs two hours every time, and a cost paid on every release gets
skipped. At ten minutes it costs twenty, on identical evidence: the same code path, exercised the same
number of times. Faster feedback rather than more waiting.

Ten minutes rather than five: preprod talks to the **real** Google Calendar API, so this interval also
sets its background API call rate against a live quota. Five would double that traffic for no extra
diagnostic value.

## Auto-Reload Mechanism

Deploys to prod cause the WebApi to restart, dropping the SignalR `CalendarHub` connection. Active WASM clients reconnect via `WithAutomaticReconnect()`; on every reconnect, `IVersionService.CheckAsync()` GETs `/api/health` and compares the server's `version` to the client's baked-in version. On mismatch, the `<UpdateBanner />` shows for 5 seconds and `location.reload()` is called. See `.agent/docs/architecture.md#versioning` for the full surface.

## Manual Test Plan: Auto-Reload on Prod

1. Trigger a master build → confirm `git tag -l v*` shows the new tag and the registry contains `:vX.Y.Z` images.
2. Run `Jenkinsfile.deploy-prod` with `DIRECTION=specific, SEMVER_TAG=vX.Y.Z`. Confirm `/api/health` reports the new version. Open the dashboard in a browser and confirm the footer reads `vX.Y.Z`.
3. Trigger another master build → `vX.Y.Z+1` lands.
4. Run deploy-prod again with the new tag.
5. The browser left open from step 2 should display the update banner within seconds of the new server coming up, then reload to `vX.Y.Z+1`.

## SCM checkout retry count (controller config)

Jenkins' global **SCM checkout retry count** is set to **4** (Manage Jenkins → System → "SCM checkout retry count"; `<scmCheckoutRetryCount>` in `$JENKINS_HOME/config.xml`). Previous value was `0` (no retry).

This makes the Git plugin retry the whole SCM checkout — the implicit Jenkinsfile-load fetch *and* every explicit `checkout scm` (build, all deploys, cleanup) — up to 4 times, so a transient GitHub-egress blip self-heals instead of failing the build or deploy. It addresses the 2026-06-10 incident (FHQ-64), where intermittent ~130 s connect-timeout blackholes to GitHub's IP failed `dev` #86, `master` #43/#44, and `Deploy-Staging` #163/#164/#165 + `Deploy-PreProd` #52 at checkout (`"Maximum checkout retry attempts reached"`).

- **Controller config, NOT version-controlled** — re-apply it if the controller is rebuilt/restored.
- **Bounded at 4** so a genuine GitHub outage still fails in finite time (~4 × the connect timeout), rather than hanging indefinitely.
- The one fetch this does *not* cover — the raw `git fetch --tags --force` in `Jenkinsfile.build`'s Compute SemVer stage (it's a shell step, not a GitSCM checkout) — has its own `retry(3)` in the Jenkinsfile.
