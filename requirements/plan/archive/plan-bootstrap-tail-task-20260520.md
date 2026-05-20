# Sub-Plan BTT-1: Local Build Data Export Task Migration

> **Risk**: Low-Medium
> **Dependencies**: `BuildPackageRequest`, `BuildContext`, `DAGScheduler`, `LocalStatusExporter`, `BuildProjectManager`, `BuildPipelineConfig`, `BuildPipelineConfigRepair`
> **Status**: Signed off — 2026-05-20
> **Source Draft**: `drafts/draft-aa-ab-task-alignment-20260519.md`
> **Positioning**: Fourth slice of AA/AB Task alignment. This plan moves full-build local data export into a Task-managed tail step in both AA and AB graphs; it does not migrate differential snapshots, Lua index export, or backend interface cleanup.

---

## Objective

Move `LocalStatusExporter.ExportData(version)` out of the direct `BuildProjectManager` post-build call and into an explicit `TaskExportLocalBuildData` at the tail of both AA and AB Task graphs.

After this plan, the full-build local data export (BuildIndex + baseline manifest to StreamingAssets) is Task-managed as the final step of both pipeline graphs, and `BuildProjectManager` no longer calls `LocalStatusExporter` directly.

---

## Background

Current verified state:

| Area | Current behavior | Problem |
|------|------------------|---------|
| AA build/finalization | AA backend runs `AABuildPipelineConfig` through `DAGScheduler` | AA package output and `AAManifest` are Task-managed |
| AB build/finalization | AB backend runs `BuildPipelineConfig` through `DAGScheduler` | AB package output and `ABManifest` are Task-managed |
| PackageIndex | `BuildProjectManager.UpdateManifestFile(request)` writes the latest package pointer after backend finalization | This remains release-orchestrator ownership |
| Bootstrap export | `BuildProjectManager.RunBuild()` calls `LocalStatusExporter.ExportData(version)` directly for `BuildType.Full` | Bootstrap output is still a direct helper call outside the Task lifecycle |
| Snapshot rebuild | `DifferentialProcessor.ReBuildSnapShots(version)` runs after bootstrap export for full builds | Snapshot ownership is not part of the bootstrap export boundary |

---

## Design Decisions

### D1: Add `TaskExportLocalBuildData` To Both AA And AB Graph Tails

Add the same task as the final node in both the AA and AB pipeline configs, after their respective manifest publication tasks.

- AA graph tail: `... -> TaskWriteAAPackageManifest -> TaskExportLocalBuildData`
- AB graph tail: `... -> TaskWriteABPackageManifest -> TaskExportLocalBuildData`

The task reads `BuildType` from `BuildContext` and skips execution (returns success) for non-Full builds.

Reason:

- Local build data export is pipeline-agnostic — it consumes the completed request and writes to StreamingAssets.
- Adding it to both graphs avoids a separate pipeline config for a single task.
- The task reference in each config is just a TaskName entry, not code duplication.

### D2: `TaskExportLocalBuildData` Responsibilities

Introduce a single tail task that:

1. reads `BuildPackageRequest` from `BuildContext`
2. reads `BuildType` from `BuildContext`; if not `Full`, returns success immediately (skip)
3. verifies `request.OutputDir` exists
4. calls `LocalStatusExporter.ExportData(request.Version)`

Reason:

- The task boundary should be complete: build-type guard, validation, execution, and diagnostics live in one task.
- `LocalStatusExporter` remains the exporter implementation; the workflow ownership moves to the Task graph.

### D3: Preserve Full-Build-Only Semantics

The bootstrap tail graph should run only for `BuildType.Full`, matching the current `BuildProjectManager` behavior.

Reason:

- Hotfix builds currently do not rewrite local bootstrap data.
- Changing hotfix bootstrap behavior would affect startup artifacts and must be treated as a separate high-risk decision.

### D4: Keep Snapshot Rebuild Outside This Plan

Keep `DifferentialProcessor.ReBuildSnapShots(version)` in `BuildProjectManager` after the bootstrap tail graph.

Reason:

- Snapshot promotion/rebuild belongs to the differential hot-update flow, not startup bootstrap export.
- Migrating it would expand this plan beyond the `LocalStatusExporter` tail-task scope.

### D5: Keep Lua Index Export And Backend Interface Cleanup Deferred

Do not move `LuaScriptsIndexExporter.ExportData()` in this plan, and do not remove `IBuildBackend.OrganizeOutput()` / `GeneratePackageManifest()`.

Reason:

- Lua index pipeline-independence is still an open design decision.
- Backend interface cleanup should happen only after package finalization and bootstrap tail behavior are both Task-managed.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Local build data task | New `TaskExportLocalBuildData` | Read BuildType from context; skip if not Full; validate request.OutputDir; call `LocalStatusExporter.ExportData(request.Version)` |
| AA pipeline config | `AABuildPipelineConfig` asset + repair | Add `TaskExportLocalBuildData` as the final tail task after `TaskWriteAAPackageManifest` |
| AB pipeline config | `BuildPipelineConfig` asset + repair | Add `TaskExportLocalBuildData` as the final tail task after `TaskWriteABPackageManifest` |
| Orchestrator | `BuildProjectManager` | Remove the direct `LocalStatusExporter.ExportData(version)` call and the `if (Full)` guard around it; the task handles the build-type check internally |
| Documentation | `README.md`, `context/architecture/resource-build-and-release.md` | Record that local build data export is Task-managed while snapshot rebuild remains orchestrator-owned |
| Progress/plan | `requirements/` | Record approval, execution, verification, and sign-off state |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| BTT1-T1 | Audit current full-build post steps and confirm exact call order around backend DAG, `PackageIndex`, local data export, and snapshot rebuild | Existing build flow |
| BTT1-T2 | Add `TaskExportLocalBuildData` with BuildType guard, request/output validation, and `LocalStatusExporter.ExportData` call | T1 |
| BTT1-T3 | Register the new task in both AA and AB pipeline config repair/default task order as the final tail task | T2 |
| BTT1-T4 | Update `BuildProjectManager` to remove the direct `LocalStatusExporter.ExportData(version)` call and the `if (Full)` guard | T2-T3 |
| BTT1-T5 | Sync README/context and requirement progress | T4 |
| BTT1-T6 | Verification: source audit for no direct `LocalStatusExporter.ExportData` call in `BuildProjectManager`, no raw I/O in touched files, new Editor task `.csproj` inclusion, and `dotnet build XLuaHotfix.sln` | T1-T5 |

---

## Invariants

1. No runtime hotfix loading behavior changes.
2. No AA or AB manifest schema changes.
3. No package directory naming changes.
4. `BuildPackageRequest` remains the single source of final output paths and package identity.
5. `BuildProjectManager` still owns `PackageIndex` update.
6. Bootstrap export remains full-build-only.
7. Hotfix builds still skip `LocalStatusExporter.ExportData()`.
8. `DifferentialProcessor.ReBuildSnapShots(version)` remains in `BuildProjectManager`.
9. `LuaScriptsIndexExporter.ExportData()` remains an outer `BuildProjectManager` call.
10. `IBuildBackend.OrganizeOutput()` and `GeneratePackageManifest()` are not removed in this plan.
11. Missing final output directory must fail with a structured build result, not silently continue.

---

## Acceptance Criteria

- [x] `TaskExportLocalBuildData` is available through the normal Task resolver.
- [x] `TaskExportLocalBuildData` reads `BuildPackageRequest` and `BuildType` from `BuildContext`.
- [x] `TaskExportLocalBuildData` skips execution (returns success) for non-Full builds.
- [x] `TaskExportLocalBuildData` validates the final package output directory before exporting.
- [x] `TaskExportLocalBuildData` is registered as the final tail task in both AA and AB pipeline configs.
- [x] Full builds produce local build data (BuildIndex + baseline manifest in StreamingAssets) through the task.
- [x] Hotfix builds do not produce local build data.
- [x] `BuildProjectManager` no longer calls `LocalStatusExporter.ExportData(version)` directly.
- [x] `DifferentialProcessor.ReBuildSnapShots(version)` still runs after backend DAG for full builds.
- [x] `LuaScriptsIndexExporter.ExportData()` remains unchanged in `BuildProjectManager`.
- [x] AA and AB backend DAG behavior (excluding the new tail task) remains unchanged.
- [x] `IBuildBackend.OrganizeOutput()` and `GeneratePackageManifest()` remain compatibility methods.
- [x] Any new Editor scripts are included in `Assembly-CSharp-Editor.csproj`.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out of Scope

- Migrating `DifferentialProcessor.ReBuildSnapShots(version)` into a Task.
- Moving `LuaScriptsIndexExporter.ExportData()` into a Task.
- Refactoring Lua address lookup away from Addressables settings.
- Removing or changing `IBuildBackend` post methods.
- Changing bootstrap file formats, `BuildIndex`, `ABManifest`, or StreamingAssets paths.
- Changing hotfix build behavior.
- CDN upload/push workflow.

---

## Approval Checklist

- [x] Add `TaskExportLocalBuildData` to both AA and AB graph tails instead of creating a separate pipeline config.
- [x] Local build data export remains full-build-only; hotfix builds continue to skip it (task-internal BuildType guard).
- [x] Keep `DifferentialProcessor.ReBuildSnapShots(version)` outside this plan and after backend DAG execution.
- [x] Keep `LuaScriptsIndexExporter.ExportData()` as the outer `BuildProjectManager` call in this plan.
- [x] Do not remove `IBuildBackend.OrganizeOutput()` / `GeneratePackageManifest()` until the later backend interface cleanup plan.
- [x] Require source audit plus `dotnet build XLuaHotfix.sln` after implementation; verify new Editor task files are included in the project file.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-20 | Promoted Plan-4 from AA/AB Task alignment draft into executable pending-approval plan |
| 2026-05-20 | Approved with adjustments: D1 changed from separate config to AA/AB graph tail; task renamed from `TaskExportBootstrapData` to `TaskExportLocalBuildData`; PackageIndex validation removed from task (handled by orchestrator before DAG); risk lowered to Low-Medium |
| 2026-05-20 | Executed. Added `TaskExportLocalBuildData` to AA/AB graph tails and removed direct `LocalStatusExporter.ExportData` call from `BuildProjectManager` |
| 2026-05-20 | Signed off by developer and archived |
