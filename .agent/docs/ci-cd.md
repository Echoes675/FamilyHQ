# CI/CD: Build, Versioning, and Deploy Pipelines

## Pipelines

| Pipeline | File | Trigger | Purpose |
|----------|------|---------|---------|
| Build | `Jenkinsfile.build` | Push on any branch | Run unit tests, build Docker images, push to registry, prune old tags |
| Deploy Dev | `Jenkinsfile.deploy-dev` | Upstream success on `dev` branch build | Resolve latest `dev-*` image and deploy to 192.168.86.23:8200 |
| Deploy Staging | `Jenkinsfile.deploy-staging` | Upstream success on `dev` or `master` build | Parameterised deploy of either branch line to staging |
| Deploy Preprod | `Jenkinsfile.deploy-preprod` | Upstream success on `master` build | Auto-deploy latest master image to preprod |
| Deploy Prod | `Jenkinsfile.deploy-prod` | Manual only | Deploy a specific image to prod, with safety checks |

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

For releases use `DIRECTION=specific` with the `vX.Y.Z` tag, so the deploy is reproducible by tag rather than by build number.

## Auto-Reload Mechanism

Deploys to prod cause the WebApi to restart, dropping the SignalR `CalendarHub` connection. Active WASM clients reconnect via `WithAutomaticReconnect()`; on every reconnect, `IVersionService.CheckAsync()` GETs `/api/health` and compares the server's `version` to the client's baked-in version. On mismatch, the `<UpdateBanner />` shows for 5 seconds and `location.reload()` is called. See `.agent/docs/architecture.md#versioning` for the full surface.

## Manual Test Plan: Auto-Reload on Prod

1. Trigger a master build → confirm `git tag -l v*` shows the new tag and the registry contains `:vX.Y.Z` images.
2. Run `Jenkinsfile.deploy-prod` with `DIRECTION=specific, SEMVER_TAG=vX.Y.Z`. Confirm `/api/health` reports the new version. Open the dashboard in a browser and confirm the footer reads `vX.Y.Z`.
3. Trigger another master build → `vX.Y.Z+1` lands.
4. Run deploy-prod again with the new tag.
5. The browser left open from step 2 should display the update banner within seconds of the new server coming up, then reload to `vX.Y.Z+1`.
