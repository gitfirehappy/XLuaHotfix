# Step 1 - Understand Intent

## Functional Requirements

### FR-1: Deterministic Windows hotfix startup

Validate the built-in baseline before any local or remote decision, require a trusted local `PackageIndex` for offline startup, allow only same-Major forward updates, repair incomplete same-package content, reject rollback and same-version package replacement, preserve the active package on update failure, and commit a new pointer only after target activation and package-manager initialization succeed.

## Assumptions

- Windows is the only implementation and acceptance target in this iteration.
- Existing Android support remains untouched; newly changed baseline inspection and reuse entry points are marked `Android deferred`.
- `FileCRC == 0` is invalid for built-in, local, and remote package manifests.
- No package schema, publish artifact format, Cloudflare state, or real AB Full/Hotfix flow is changed.
- Existing uncommitted workspace changes are preserved.
