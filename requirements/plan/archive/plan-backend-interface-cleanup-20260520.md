# Sub-Plan BIC-1: Backend Interface Cleanup After Task Alignment

> **Risk**: Medium
> **Dependencies**: `IBuildBackend`, `BuildProjectManager`, `ABBuildBackend`, `AAAddressableBuildBackend`, `BuildPackageRequest`, `DAGScheduler`, `TaskWriteABPackageManifest`, `TaskWriteAAPackageManifest`, `TaskExportLocalBuildData`
> **Status**: Signed off — 2026-05-20
> **Source Draft**: `drafts/archive/draft-aa-ab-task-alignment-20260519.md`
> **Positioning**: Final slice of AA/AB Task alignment. Package finalization and full-build local data export are now Task-managed; this plan removes backend post-build compatibility methods and leaves backend implementations as DAG runners.

---

## Objective

Remove the obsolete backend post-build interface surface now that AA and AB build/finalization flows are task-managed.

After this plan, `IBuildBackend` exposes build execution only. `BuildProjectManager` will run the selected backend DAG once, update `PackageIndex`, and keep only orchestrator-owned release steps such as snapshot rebuild.

---

## Background

Current verified state:

| Area | Current behavior | Cleanup target |
|------|------------------|----------------|
| AB backend DAG | `TaskOrganizeOutput`, `TaskWriteABPackageManifest`, and `TaskExportLocalBuildData` own AB final package layout, manifest publication, and full-build local data export | `ABBuildBackend.OrganizeOutput()` / `GeneratePackageManifest()` are compatibility validation only |
| AA backend DAG | `TaskBuildAddressablesContent`, `TaskOrganizeAAOutput`, `TaskWriteAAPackageManifest`, and `TaskExportLocalBuildData` own AA build/finalization/local data export | `AAAddressableBuildBackend.OrganizeOutput()` / `GeneratePackageManifest()` are compatibility validation only |
| Orchestrator | `BuildProjectManager` still calls `backend.OrganizeOutput(...)` and `backend.GeneratePackageManifest(...)` after `BuildAsync()` | These calls no longer produce artifacts |
| Interface | `IBuildBackend` still requires `OrganizeOutput()` and `GeneratePackageManifest()` | Interface contract no longer matches the task-managed architecture |

---

## Design Decisions

### D1: `IBuildBackend` Becomes Build-Only

Remove these methods from `IBuildBackend`:

- `void OrganizeOutput(string outputDir, VersionNumber version)`
- `void GeneratePackageManifest(string outputDir, VersionNumber version)`

Reason:

- Final output and manifest publication now live in the AA/AB DAGs.
- Keeping post methods suggests a second output ownership path and risks future drift.

### D2: Remove Backend Compatibility Implementations

Delete the compatibility-only methods and their private validation helpers from both concrete backends.

Reason:

- They no longer add production behavior after BTT-1.
- Any required output validation must stay in the task that owns the artifact.

### D3: Remove Convenience Overloads And Keep Request-Based Build Execution Only

Remove the convenience overloads from both `IBuildBackend` and concrete backends:

- `BuildAsync(VersionNumber version, BuildType buildType)`
- `BuildAsync(VersionNumber version, BuildType buildType, BuildExecutionOptions options)`

Keep only `BuildAsync(BuildPackageRequest request, BuildExecutionOptions options)` as the single backend API.

Reason:

- No external callers use the convenience overloads; only `BuildProjectManager` calls `BuildAsync(request, options)`.
- Removing them reduces interface surface and eliminates dead code.
- Backend core responsibility is DAG execution from a prepared request, nothing more.

### D4: Preserve Orchestrator-Owned Steps

Keep these in `BuildProjectManager`:

- `LuaScriptsIndexExporter.ExportData()`
- `BuildPackageRequest.Create(...)`
- backend selection
- `UpdateManifestFile(request)`
- full-build `DifferentialProcessor.ReBuildSnapShots(version)`

Reason:

- These are outside the backend post-method cleanup scope.
- Lua index pipeline-independence remains a separate deferred design decision.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Backend interface | `IBuildBackend` | Remove `OrganizeOutput()`, `GeneratePackageManifest()`, and convenience `BuildAsync` overloads; keep only `BuildAsync(BuildPackageRequest, BuildExecutionOptions)` |
| Orchestrator | `BuildProjectManager` | Remove post-`BuildAsync()` calls to `backend.OrganizeOutput()` and `backend.GeneratePackageManifest()` |
| AB backend | `ABBuildBackend` | Remove compatibility post methods, convenience overloads, validation helpers, and now-unused fields (`_manifest`, `_finalOutputDir`, `_context`, `_request` become local); backend becomes stateless DAG runner |
| AA backend | `AAAddressableBuildBackend` | Same as AB: remove post methods, convenience overloads, validation helpers, and unused fields; backend becomes stateless DAG runner |
| Documentation | `README.md`, `context/architecture/resource-build-and-release.md` | Record that backends are stateless DAG runners with a single `BuildAsync(request, options)` entry point |
| Progress/plan | `requirements/` | Record approval, execution, verification, sign-off, and archive state |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| BIC1-T1 | Audit all `OrganizeOutput()` and `GeneratePackageManifest()` call sites and confirm only backend interface/implementations/orchestrator references remain | Current code |
| BIC1-T2 | Remove post-build methods from `IBuildBackend` and delete corresponding calls in `BuildProjectManager` | T1 |
| BIC1-T3 | Remove compatibility method implementations and now-unused backend fields/helpers from AA/AB backends | T2 |
| BIC1-T4 | Sync README/context/progress with the build-only backend contract | T3 |
| BIC1-T5 | Verification: grep for removed method names, confirm `BuildProjectManager` still updates `PackageIndex` and snapshots, confirm AA/AB DAG tasks still own finalization, and run `dotnet build XLuaHotfix.sln` | T1-T4 |

---

## Invariants

1. No runtime hotfix loading behavior changes.
2. No AA or AB manifest schema changes.
3. No package directory naming changes.
4. `BuildPackageRequest` remains the single source of final package output paths and package identity.
5. AA and AB final package layout remains DAG-owned.
6. AA and AB manifest publication remains DAG-owned.
7. Full-build local data export remains `TaskExportLocalBuildData` owned.
8. `BuildProjectManager` still updates `PackageIndex` after backend build success.
9. `BuildProjectManager` still runs snapshot rebuild for full builds.
10. `LuaScriptsIndexExporter.ExportData()` remains unchanged and outside the backend DAG.
11. No fallback helper path may recreate output organization or manifest publication outside the DAG.

---

## Acceptance Criteria

- [x] `IBuildBackend` no longer declares `OrganizeOutput()` or `GeneratePackageManifest()`.
- [x] `IBuildBackend` no longer declares convenience `BuildAsync(VersionNumber, BuildType)` overloads.
- [x] `IBuildBackend` declares only `BuildAsync(BuildPackageRequest, BuildExecutionOptions)`.
- [x] `BuildProjectManager` no longer calls backend post-build output/manifest methods.
- [x] `ABBuildBackend` is stateless: no instance fields, only `BuildAsync` + `LogBuildResultErrors`.
- [x] `AAAddressableBuildBackend` is stateless: no instance fields, only `BuildAsync` + `LogBuildResultErrors`.
- [x] AA and AB backends still execute their configured DAGs from `BuildAsync(BuildPackageRequest, BuildExecutionOptions)`.
- [x] `BuildProjectManager.UpdateManifestFile(request)` remains after backend build success.
- [x] `DifferentialProcessor.ReBuildSnapShots(version)` remains full-build-only.
- [x] `LuaScriptsIndexExporter.ExportData()` remains unchanged in `BuildProjectManager`.
- [x] README and `context/architecture/resource-build-and-release.md` reflect the stateless DAG-runner backend contract.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out of Scope

- Moving `LuaScriptsIndexExporter.ExportData()` into a task.
- Refactoring Lua address lookup away from Addressables settings.
- Migrating `DifferentialProcessor.ReBuildSnapShots(version)` into a task.
- Changing AA/AB task graph semantics.
- Changing `BuildPackageRequest` naming or output layout.
- Changing manifest schemas or bootstrap file formats.
- Runtime loading or hotfix update behavior changes.
- CDN upload/push workflow.

---

## Approval Checklist

- [x] Remove `OrganizeOutput()` and `GeneratePackageManifest()` from `IBuildBackend`.
- [x] Remove convenience `BuildAsync` overloads from `IBuildBackend` and both backends; keep only `BuildAsync(BuildPackageRequest, BuildExecutionOptions)`.
- [x] Remove all now-unused fields and helpers from both backends; backends become stateless DAG runners.
- [x] Keep `BuildProjectManager.UpdateManifestFile(request)` in the orchestrator after backend build success.
- [x] Keep full-build `DifferentialProcessor.ReBuildSnapShots(version)` in `BuildProjectManager`.
- [x] Keep `LuaScriptsIndexExporter.ExportData()` unchanged and outside this cleanup.
- [x] Verify by grep for removed method names plus `dotnet build XLuaHotfix.sln`.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-20 | Promoted final backend interface cleanup scope from archived AA/AB Task alignment draft into executable pending-approval plan |
| 2026-05-20 | Approved with adjustments: D3 changed from "keep convenience overloads" to "delete them"; backends become fully stateless (no instance fields) |
| 2026-05-20 | Executed. `IBuildBackend` now exposes only request-based `BuildAsync`; AA/AB backends are stateless DAG runners |
| 2026-05-20 | Signed off by developer and archived |
