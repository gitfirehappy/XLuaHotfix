# Sub-Plan ABF-1: AB Finalization Task Migration

> **Risk**: Medium
> **Dependencies**: `BuildPackageRequest`, AB `BuildContext`, `DAGScheduler`, `TaskGenerateManifest`, `TaskOrganizeOutput`, `ABBuildBackend`, `HotfixPackageSizeGuard`, `ManifestOutputFormat`
> **Status**: Signed off — 2026-05-20
> **Source Draft**: `drafts/draft-aa-ab-task-alignment-20260519.md`
> **Positioning**: Second slice of AA/AB Task alignment. This plan moves AB final output organization and AB manifest emission into the AB Task graph; it does not migrate AA or remove backend interface methods yet.

---

## Objective

Make the AB build Task graph produce the final release package directly under `BuildPackageRequest.OutputDir`.

After this plan, AB finalization is no longer split between `TaskOrganizeOutput` and backend post-build methods. The DAG owns AB bundle copy, package layout, manifest JSON/binary emission, size guard, summary generation, and output path publication as one complete finalization boundary.

---

## Background

Current verified state:

| Area | Current behavior | Problem |
|------|------------------|---------|
| AB request | `ABBuildBackend.BuildAsync(BuildPackageRequest, ...)` writes `BuildPackageRequest` into `BuildContext` | Finalization tasks can now consume the final output directory, but do not yet do so |
| `TaskOrganizeOutput` | Copies bundles from `BuildConfig.OutputRoot/_temp` into `BuildConfig.OutputRoot/BuildVersionString`, writes `ABManifest.json`, writes `build_summary.txt`, and cleans `_temp` | It still writes pipeline-local output, not the final package output from the request |
| `ABBuildBackend.OrganizeOutput()` | Copies the pipeline-local output into `BuildPackageRequest.OutputDir` and bundles into `request.BundlesDir` | Backend still owns duplicated final package layout logic |
| `ABBuildBackend.GeneratePackageManifest()` | Applies size guard and writes `ABManifest.json` / `ABManifest.bin` according to `FYAssetSettings.ManifestOutputFormat` | Manifest emission is outside the Task graph and duplicates task-side JSON writing |
| Missing bundle handling | `TaskGenerateManifest` fails when temp bundle file is missing; `TaskOrganizeOutput` silently skips missing files during copy | Finalization can produce incomplete output without failing at the copy boundary |

---

## Design Decisions

### D1: AB Final Output Uses `BuildPackageRequest`

AB finalization tasks must read `BuildContextKeys.BuildPackageRequest` and write final artifacts under:

- `request.OutputDir`
- `request.BundlesDir`

Reason:

- BOU-1 made the build request the single owner of package identity and final paths.
- Continuing to use `BuildConfig.OutputRoot/BuildVersionString` would keep two output truths alive.

### D2: Keep Finalization Complete Inside The Task Graph

This plan must finish AB finalization inside the DAG, including:

- cleaning/recreating final AB package output when appropriate
- copying all manifest-listed bundles into `request.BundlesDir`
- writing manifest JSON and/or binary according to `ManifestOutputFormat`
- applying `HotfixPackageSizeGuard`
- writing `build_summary.txt`
- cleaning the AB `_temp` directory
- setting `BuildContextKeys.OutputPath` to `request.OutputDir`

Reason:

- A partial move would leave output ownership split across Task and backend layers.
- Future AA migration needs AB to demonstrate the complete task-managed boundary first.

### D3: Split Manifest Writing From Bundle Organization

Prefer a dedicated `TaskWriteABPackageManifest` after output organization rather than keeping manifest emission hidden inside `TaskOrganizeOutput`.

Intended AB tail order:

1. `TaskGenerateManifest`
2. `TaskVerifyBuildResult`
3. `TaskOrganizeOutput`
4. `TaskWriteABPackageManifest`

Reason:

- `TaskOrganizeOutput` should own package directory layout and bundle copy.
- Manifest output format and size guard are a separate release artifact concern.
- Separate tasks reduce future AA/AB sharing pressure and keep each task auditable.

### D4: Backend Post Methods Remain For Interface Compatibility Only

Do not remove `IBuildBackend.OrganizeOutput()` or `IBuildBackend.GeneratePackageManifest()` in this plan.

For AB, the methods should no longer duplicate normal output work after a successful DAG run. They may validate that the request and DAG output are available and already point to the expected final output.

Reason:

- Interface cleanup belongs after both AB and AA are task-managed.
- `BuildProjectManager` can keep its current call sequence without causing duplicate AB writes.

### D5: Missing Finalization Inputs Are Fatal

Any bundle listed in `ABManifest.BundleEntries` but missing from the temp build output must fail finalization.

Reason:

- Silent skip matches historical mistake IP-01 and can create runtime-only failures.
- Finalization is the last safe point to detect incomplete package output.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| AB finalization task | `TaskOrganizeOutput` | Make AB output organization consume `BuildPackageRequest`, copy bundles to `request.BundlesDir`, fail on missing bundle files, write summary to `request.OutputDir`, clean `_temp`, and set `OutputPath = request.OutputDir` |
| AB manifest output task | New `TaskWriteABPackageManifest` | Write `ABManifest.json` / `ABManifest.bin` according to `FYAssetSettings.ManifestOutputFormat`, apply `HotfixPackageSizeGuard`, and remove excluded format files |
| AB DAG repair/default list | `BuildPipelineConfigRepair` and related task registration paths | Add the new manifest output task after `TaskOrganizeOutput` for AB pipeline configs |
| AB backend | `ABBuildBackend` | Stop copying bundles and writing AB manifests in normal post-build calls; retain validation and compatibility behavior |
| Build orchestration | `BuildProjectManager` interaction with AB backend | Keep call order compatible while ensuring AB backend post methods do not duplicate task-managed output |
| Documentation | `README.md`, `context/architecture/resource-build-and-release.md` | After execution, record that AB finalization is DAG-owned while AA remains backend/helper-owned for now |
| Mistake prevention | `context/mistakes/implementation-pitfalls.md` if needed | Add or extend a verified note only if execution uncovers a reusable mistake beyond existing IP-01/IP-16/IP-17 |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| ABF1-T1 | Audit the current AB DAG tail and confirm all call sites that read `BuildContextKeys.OutputPath` | Existing AB task graph |
| ABF1-T2 | Update `TaskOrganizeOutput` to require `BuildPackageRequest` and write the final package layout under request-owned paths | T1 |
| ABF1-T3 | Make `TaskOrganizeOutput` fail fast when a manifest-listed bundle is missing from `_temp` | T2 |
| ABF1-T4 | Add `TaskWriteABPackageManifest` for JSON/bin emission, size guard, and excluded-format cleanup | T2 |
| ABF1-T5 | Register the new task in AB pipeline config repair/default task order after `TaskOrganizeOutput` | T4 |
| ABF1-T6 | Update `ABBuildBackend` post methods so normal AB final output is not copied or written twice | T2-T5 |
| ABF1-T7 | Sync README/context with the verified AB finalization ownership boundary | T6 |
| ABF1-T8 | Verification: source audit for duplicate AB manifest writes/copies, missing-bundle fail-fast path, `.csproj` inclusion for any new task file, and `dotnet build XLuaHotfix.sln` | T1-T7 |

---

## Invariants

1. No AA build graph or Addressables build behavior changes.
2. No runtime hotfix loading behavior changes.
3. No AB manifest schema changes.
4. No package index ownership changes; `BuildProjectManager` still updates `PackageIndex`.
5. `BuildPackageRequest` remains the single source of final AB package output paths.
6. `BuildExecutionOptions` remains execution/progress options, not output identity storage.
7. AB final package must stay complete after `BuildProjectManager.RunBuild()` returns successfully.
8. `IBuildBackend.OrganizeOutput()` and `IBuildBackend.GeneratePackageManifest()` are not removed in this plan.
9. `TaskGenerateManifest` remains responsible for constructing `ABManifest`; final output tasks only publish it.
10. Missing required build artifacts must fail the build instead of being skipped.

---

## Acceptance Criteria

- [x] AB DAG writes final package output under `BuildPackageRequest.OutputDir`.
- [x] AB bundles are copied into `BuildPackageRequest.BundlesDir`.
- [x] `BuildContextKeys.OutputPath` is set to `BuildPackageRequest.OutputDir` after AB finalization.
- [x] `ABManifest.json` and `ABManifest.bin` are emitted according to `FYAssetSettings.ManifestOutputFormat`.
- [x] `HotfixPackageSizeGuard` is applied before AB manifest publication succeeds.
- [x] `build_summary.txt` is still generated in the final AB package directory.
- [x] Missing bundle files during AB finalization produce a failed `BuildTaskResult`, not a silent skip.
- [x] `ABBuildBackend.OrganizeOutput()` no longer copies AB bundles during the normal request-driven build path.
- [x] `ABBuildBackend.GeneratePackageManifest()` no longer writes AB manifest files during the normal request-driven build path.
- [x] No duplicate AB manifest write remains between task and backend layers.
- [x] No duplicate final AB bundle copy remains between task and backend layers.
- [x] AA backend/helper output flow remains unchanged.
- [x] Any new Editor script is included in `Assembly-CSharp-Editor.csproj` before relying on `dotnet build`.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out of Scope

- Migrating AA Addressables build/output/manifest stages into a DAG.
- Adding the bootstrap tail Task.
- Removing `IBuildBackend.OrganizeOutput()` or `IBuildBackend.GeneratePackageManifest()`.
- Changing `BuildProjectManager` package index ownership.
- Changing AB manifest data model or serialization schema.
- Changing hotfix download/runtime loading behavior.
- Changing Addressables group movement, restore, or confirm-release behavior.
- Build Repository integration.
- CDN upload/push workflow.

---

## Approval Checklist

- [x] AB final package output should be written directly by the AB Task graph under `BuildPackageRequest.OutputDir`.
- [x] AB bundles should be copied into `BuildPackageRequest.BundlesDir`, not into `BuildConfig.OutputRoot/BuildVersionString`.
- [x] Add a separate `TaskWriteABPackageManifest` after `TaskOrganizeOutput` instead of keeping manifest publication inside the organizer.
- [x] Keep `IBuildBackend.OrganizeOutput()` and `IBuildBackend.GeneratePackageManifest()` for compatibility in this plan, but make AB normal path no-op/validation-only to avoid duplicate output.
- [x] Treat any missing manifest-listed bundle during finalization as a fatal build failure.
- [x] Keep AA build/output behavior unchanged in this plan.
- [x] Do not remove or redesign backend interfaces until both AA and AB finalization are task-managed.
- [x] Run source audit plus `dotnet build XLuaHotfix.sln` after implementation; verify new Editor task file is included in the project file.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-20 | Promoted Plan-2 from AA/AB Task alignment draft into executable pending-approval plan |
| 2026-05-20 | Approved. Decisions: D3 split confirmed, D4 no-op safe (no non-request path exists), D5 fail-fast confirmed, DAG tail order confirmed |
| 2026-05-20 | Executed. AB final output and manifest publication are Task-managed; backend post methods are compatibility validation only |
| 2026-05-20 | Signed off by developer and archived |
