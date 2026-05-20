# Sub-Plan BOU-1: Build Request And Output Ownership Unification

> **Risk**: Low-Medium
> **Dependencies**: Existing `BuildProjectManager`, `IBuildBackend`, `BuildPathManager`, `BuildExecutionOptions`, AB `DAGScheduler`, AA/AB build backends
> **Status**: Executed — 2026-05-20, awaiting developer sign-off
> **Source Draft**: `drafts/draft-aa-ab-task-alignment-20260519.md`
> **Positioning**: First slice of AA/AB Task alignment. This plan only unifies build request data and final output ownership; it does not Task-ify AA or AB output stages yet.

---

## Objective

Make the final build package identity and output directory a single explicit build request prepared before backend execution.

After this plan, downstream AA/AB Task graphs can safely consume the same package name, output directory, version, build type, and backend mode without recomputing or guessing them. This is the prerequisite for later moving output organization and manifest emission fully into Task graphs.

---

## Background

Current verified state:

| Area | Current behavior | Problem |
|------|------------------|---------|
| Package name | `BuildProjectManager.RunBuild()` computes `Build_{date}_{version}` after `backend.BuildAsync()` | Tasks executed inside backend cannot know the final package directory |
| Output directory | `BuildProjectManager` creates `outputDir` after backend execution, then calls `backend.OrganizeOutput(outputDir, version)` | Output ownership is split between orchestrator and backend post-steps |
| AB DAG | AB `BuildContext` currently gets its own `BuildConfig` and writes `OutputPath` from `TaskOrganizeOutput` | Future final output Task would conflict with `BuildProjectManager` if paths remain independently computed |
| AA backend | AA backend stores `_serverDataPath`, then later receives `outputDir` from `BuildProjectManager` | AA cannot be moved into a DAG cleanly until the final output path is part of the request/context |
| `BuildExecutionOptions` | Used for task execution status reporting | It should not become the source of build identity or output paths |

---

## Design Decisions

### D1: Add A Dedicated Build Request Model

Introduce an Editor-only request model, tentatively named `BuildPackageRequest`.

It should carry:

- `VersionNumber Version`
- `BuildType BuildType`
- `BackendMode BackendMode`
- `string PackageName`
- `string OutputDir`
- `string BundlesDir`
- `string PackageIndexPath`
- `DateTime CreatedAt`

Reason:

- A request model separates build identity from progress/event options.
- Later AA/AB DAG tasks can read the same request object from `BuildContext`.
- It prevents package path recomputation drift across backend, tasks, and manifest output.

### D2: `BuildProjectManager` Owns Package Identity Creation

`BuildProjectManager` remains the outer release orchestrator and creates the request before calling backend build.

Reason:

- It already owns version increment, backend routing, PackageIndex update, and full/hotfix entry points.
- Package naming is release orchestration, not backend-specific build logic.

### D3: Backends Receive The Request But Do Not Yet Become Thin Runners

Extend `IBuildBackend` with request-aware build entry points while keeping the old post-build methods for this sub-plan.

This plan may keep compatibility overloads during migration, but new internal calls should use the request-aware path.

Reason:

- Removing `OrganizeOutput()` / `GeneratePackageManifest()` belongs to later plans after AB and AA finalization are Task-managed.
- This plan must leave the build flow complete and runnable.

### D4: AB BuildContext Gets The Same Request

`ABBuildBackend` should write the request into `BuildContext` before executing `DAGScheduler`.

Reason:

- Later AB finalization tasks can consume the final package directory without changing `BuildProjectManager` again.
- This avoids hidden coupling through `FYAssetSettings` or duplicated path math.

### D5: AA Backend Stores The Same Request

`AAAddressableBuildBackend` should receive and store the request for later AA DAG migration.

Reason:

- AA remains helper/backend-driven in this plan, but the output path source becomes aligned now.
- Later AA task extraction can use the existing request contract instead of inventing a second path contract.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Request model | New Editor-only build release/shared model | Add `BuildPackageRequest` with final package identity and output paths |
| Context key | `BuildContextKeys` | Add a key for the build package request |
| Orchestration | `BuildProjectManager` | Create request before backend execution and use its `OutputDir` for package output |
| Backend interface | `IBuildBackend` | Add request-aware `BuildAsync(BuildPackageRequest, BuildExecutionOptions)` path while preserving compatibility as needed |
| AB backend | `ABBuildBackend` | Store request into `BuildContext`; keep existing AB behavior otherwise |
| AA backend | `AAAddressableBuildBackend` | Store request for output/manifest calls; keep existing AA behavior otherwise |
| Path usage | Build output call sites | Replace local recomputation of package output paths with request fields where applicable |
| Docs/context | `README.md`, `context/architecture/resource-build-and-release.md` | Record that package identity/output directory is now owned by `BuildProjectManager` request creation |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| BOU1-T1 | Add `BuildPackageRequest` model and constructor/factory that derives package paths from `BuildPathManager` | Existing `BuildPathManager` |
| BOU1-T2 | Add `BuildContextKeys.BuildPackageRequest` for DAG consumption | T1 |
| BOU1-T3 | Update `IBuildBackend` with request-aware build entry point and compatibility wrappers | T1 |
| BOU1-T4 | Update `BuildProjectManager.RunBuild()` to create one request before backend execution and use request output paths | T3 |
| BOU1-T5 | Update `ABBuildBackend` to accept the request and write it into `BuildContext` before `DAGScheduler.Execute()` | T2-T3 |
| BOU1-T6 | Update `AAAddressableBuildBackend` to accept and retain the request without changing Addressables build behavior | T3 |
| BOU1-T7 | Sync README/context with the verified ownership boundary | T4-T6 |
| BOU1-T8 | Verification: source audit for duplicate package path computation, compile, and AA/AB build-flow call graph inspection | T1-T7 |

---

## Invariants

1. No runtime loading behavior changes.
2. No build artifact format changes.
3. No Addressables core API replacement.
4. AA hotfix group movement behavior remains unchanged.
5. AB DAG task list and execution order remain unchanged unless only needed to read the request key safely.
6. `BuildProjectManager` remains the outer release orchestrator.
7. `BuildExecutionOptions` remains execution/progress options, not build identity storage.
8. `PackageIndex` update still happens once through `BuildProjectManager`.
9. Later plans may remove backend post-build methods, but this plan must not leave output organization or manifest generation half-migrated.

---

## Acceptance Criteria

- [x] `BuildProjectManager` creates exactly one build package request per `RunBuild()` call before backend execution.
- [x] Package name, output directory, bundles directory, version, build type, and backend mode are available from the request.
- [x] AB backend writes the request into `BuildContext`.
- [x] AA backend receives the same request and uses request-owned output path data where applicable.
- [x] Existing full build and hotfix build entry points still route through `BuildProjectManager`.
- [x] Existing AA Addressables build, AA hotfix preparation, AB DAG execution, output organization, manifest generation, and PackageIndex update still run in the same behavioral order.
- [x] Source audit finds no new duplicated package-name or package-output path computation in backend code.
- [x] `dotnet build XLuaHotfix.sln` or Unity Editor compilation passes with 0 new errors.

---

## Out of Scope

- Moving AB output organization into a finalization Task.
- Moving AA Addressables build/output/manifest stages into a DAG.
- Removing `IBuildBackend.OrganizeOutput()` or `IBuildBackend.GeneratePackageManifest()`.
- Changing AA/AB manifest schemas or output formats.
- Changing runtime hotfix loading behavior.
- Changing Addressables group movement, restore, or confirm-release behavior.
- Build Repository integration.
- CDN upload/push workflow.

---

## Approval Checklist

- [x] Add a dedicated `BuildPackageRequest` model instead of storing package identity in `BuildExecutionOptions`.
- [x] Let `BuildProjectManager` create package name and final output paths before backend execution.
- [x] Keep `OrganizeOutput()` and `GeneratePackageManifest()` for this plan; remove them only in later finalization plans.
- [x] Write the request into AB `BuildContext` for future Task finalization.
- [x] Pass the same request into AA backend without changing Addressables build behavior.
- [x] Keep AA hotfix group movement and PackageIndex update behavior unchanged.
- [x] Treat this as the prerequisite slice for later AB/AA Task finalization, not as a partial output-stage migration.
- [x] Package name timestamp shifts from post-build to pre-build (request creation time); this is an accepted behavioral change.
- [x] `UpdateManifestFile` call in `BuildProjectManager` must also read `PackageName` from the request (covered by T4 scope).

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-20 | Promoted Plan-1 from AA/AB Task alignment draft into executable pending-approval plan |
| 2026-05-20 | Approved. Added checklist items: timestamp shift declaration (A), UpdateManifestFile uses request (C, T4 scope). SizeGuard confirmed path-independent — no change needed |
| 2026-05-20 | Executed; added `BuildPackageRequest`, request-aware backend entry point, AB `BuildContext` request key, request-owned PackageIndex update, docs/context sync, and verification |
