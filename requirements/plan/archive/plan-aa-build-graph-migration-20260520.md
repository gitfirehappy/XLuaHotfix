# Sub-Plan AAG-1: AA Build Graph Migration

> **Risk**: Medium-High
> **Dependencies**: `BuildPackageRequest`, `BuildContext`, `DAGScheduler`, `AAAddressableBuildBackend`, `LuaScriptsIndexExporter`, `AddressablesBuildOutputOrganizer`, `AAAssetIndexBuilder`, `AAManifest`, `HotfixPackageSizeGuard`, `ManifestOutputFormat`
> **Status**: Signed off — 2026-05-20
> **Source Draft**: `drafts/draft-aa-ab-task-alignment-20260519.md`
> **Positioning**: Third slice of AA/AB Task alignment. This plan moves the AA Addressables build and AA package finalization into an AA Task graph; it does not add the bootstrap tail task or remove backend interface methods yet.

---

## Objective

Make the AA Addressables release path execute through `IBuildTask` + `BuildContext` + `DAGScheduler`, with `BuildPackageRequest` as the single source of final package output paths.

After this plan, AA and AB both use task-managed build/finalization flows. AA-specific Addressables work remains AA-specific, but it is no longer hidden behind backend helper calls after `BuildAsync()`.

---

## Background

Current verified state:

| Area | Current behavior | Problem |
|------|------------------|---------|
| Build request | `BuildProjectManager` creates one `BuildPackageRequest` before backend execution | AA can consume request-owned paths in a DAG |
| AB path | AB final package layout and AB manifest publication are Task-managed | AA remains backend/helper-managed, so AA/AB finalization boundaries differ |
| Lua index | `BuildProjectManager.RunBuild()` calls `LuaScriptsIndexExporter.ExportData()` before backend selection | Lua index export is still an outer special case, not a pipeline task |
| AA build | `AAAddressableBuildBackend.BuildAsync()` configures Addressables and calls `AddressableAssetSettings.BuildPlayerContent()` directly | Build execution is not visible to the shared task scheduler |
| AA output | `AAAddressableBuildBackend.OrganizeOutput()` calls `AddressablesBuildOutputOrganizer.OrganizeBuildOutput()` after backend build | Final package layout remains a backend post step |
| AA manifest | `AAAddressableBuildBackend.GeneratePackageManifest()` scans final bundles, builds AA asset index, applies size guard, and writes `AAManifest.json/bin` | Manifest emission remains a backend post step |

---

## Design Decisions

### D1: Add A Separate AA Pipeline Config Asset

Introduce an AA `BuildPipelineConfig` asset and a settings path for it instead of reusing the AB pipeline graph.

Recommended names:

- `Assets/Build/AABuildPipelineConfig.asset`
- `Assets/Build/ABBuildPipelineConfig.asset` if the current config is renamed in a later cleanup, or keep the current `BuildPipelineConfig.asset` as the AB config for this plan

Reason:

- AA and AB use the same task engine but not the same task list.
- Sharing one config would force disabled tasks or backend-mode branching into every graph.
- The draft explicitly converged on separate AA and AB pipeline config assets.

### D2: AA Backend Becomes A DAG Runner For AA Work

`AAAddressableBuildBackend.BuildAsync(BuildPackageRequest, BuildExecutionOptions)` should create a `BuildContext`, write the request into it, load the AA pipeline config, and execute `DAGScheduler`.

Reason:

- This matches the AB backend boundary after ABF-1.
- It keeps `BuildProjectManager` as the release orchestrator while moving AA work into tasks.

### D3: LuaScriptsIndexExporter Remains Outside The AA Graph (Deferred)

`LuaScriptsIndexExporter.ExportData()` stays as a `BuildProjectManager` outer call for both AA and AB builds. It is not moved into the AA graph in this plan.

Reason:

- The exporter depends on Addressables API for address lookup and group registration.
- AB runtime also needs `LuaScriptsIndex` (loaded by address via `ABPackageBackend`).
- Making it pipeline-agnostic requires redesigning the address source, which is a separate optimization.
- Keeping the outer call ensures both pipelines have an up-to-date index before build.

### D4: Split AA Graph Into Complete Build And Finalization Tasks

The AA graph should include these task responsibilities:

1. configure Addressables settings
2. clean ServerData
3. run `AddressableAssetSettings.BuildPlayerContent`
4. organize final package output under `BuildPackageRequest.OutputDir`
5. build AA asset index
6. publish `AAManifest.json` / `AAManifest.bin`

Reason:

- Moving only `BuildPlayerContent` into a task would leave output and manifest ownership split.
- A complete AA graph is required before backend interface cleanup is safe.

### D5: Keep Backend Post Methods For Compatibility Only

Do not remove `IBuildBackend.OrganizeOutput()` or `GeneratePackageManifest()` in this plan.

For AA, the methods should become validation-only in the normal request-driven path after the AA DAG completes, matching AB.

Reason:

- Interface cleanup belongs after both AA and AB pipelines are task-managed.
- This keeps `BuildProjectManager` call order stable during migration.

### D6: Preserve AA Addressables Behavior

Do not change Addressables group movement, `BuildRemoteCatalog`, `PackTogetherByLabel`, LuaScripts remote path repair, full/hotfix versioning, or PackageIndex ownership.

Reason:

- This plan changes the ownership boundary, not AA artifact semantics.
- Hot-update behavior is high-risk and must not drift during structural migration.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Settings/config | `FYAssetSettings`, build config assets | Add/route AA pipeline config path while preserving existing AB config behavior |
| AA backend | `AAAddressableBuildBackend` | Run the AA DAG with `BuildPackageRequest` in `BuildContext`; keep post methods as compatibility validation only |
| Addressables build task | New `TaskBuildAddressablesContent` | Configure Addressables, clean ServerData, execute `BuildPlayerContent`, and write ServerData path/result into context |
| AA output task | New `TaskOrganizeAAOutput` | Copy ServerData output into `request.OutputDir`, bundles into `request.BundlesDir`, and set `BuildContextKeys.OutputPath` |
| AA manifest task | New `TaskWriteAAPackageManifest` | Build AA asset index, scan final bundles, apply `HotfixPackageSizeGuard`, compute `FileHash`, and write/remove `AAManifest.json/bin` according to `ManifestOutputFormat` |
| Documentation | `README.md`, `context/architecture/resource-build-and-release.md` | Record that AA finalization is DAG-owned after execution |
| Progress/plan | `requirements/` | Record approval, execution, verification, and sign-off state |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| AAG1-T1 | Audit AA backend helper responsibilities and define AA context keys for ServerData path, Addressables result, AA manifest, and output path | Existing AA backend |
| AAG1-T2 | Add or route a dedicated AA `BuildPipelineConfig` asset and repair/default task list for the AA graph | T1 |
| AAG1-T3 | Add Addressables build task for settings configuration, ServerData cleanup, and `BuildPlayerContent` execution | T2 |
| AAG1-T4 | Add AA output organization task that writes final package layout under `BuildPackageRequest.OutputDir` | T3 |
| AAG1-T5 | Add AA manifest publication task for `AAManifest.json/bin`, AA asset index data, manifest file hash, and size guard | T4 |
| AAG1-T6 | Update `AAAddressableBuildBackend` to run the AA DAG and make post methods validation-only for normal request-driven AA builds | T3-T5 |
| AAG1-T7 | Sync README/context and requirement progress | T6 |
| AAG1-T8 | Verification: source audit for duplicate AA output/manifest work, raw I/O in touched files, `.csproj` inclusion for new Editor scripts, and `dotnet build XLuaHotfix.sln` | T1-T7 |

---

## Invariants

1. No runtime hotfix loading behavior changes.
2. No AA manifest schema changes.
3. No AB build graph or AB finalization behavior changes.
4. No PackageIndex ownership changes; `BuildProjectManager` still updates it once.
5. `BuildPackageRequest` remains the single source of final package output paths.
6. `BuildExecutionOptions` remains execution/progress options, not output identity storage.
7. `IBuildBackend.OrganizeOutput()` and `GeneratePackageManifest()` are not removed in this plan.
8. AA full/hotfix version increment and `DifferentialProcessor.PrepareHotfix()` behavior remain in `BuildProjectManager`.
9. AA Addressables settings semantics (`BuildRemoteCatalog`, LuaScripts remote path, bundle packing mode) must remain unchanged.
10. Missing or invalid required AA build artifacts must fail with structured build results instead of silent skips.
11. `LuaScriptsIndexExporter.ExportData()` outer call in `BuildProjectManager` remains unchanged.

---

## Acceptance Criteria

- [x] AA backend executes an AA `BuildPipelineConfig` through `DAGScheduler`.
- [x] `BuildPackageRequest` is written into AA `BuildContext`.
- [x] Addressables `BuildPlayerContent` execution is task-managed.
- [x] AA final package output is written under `BuildPackageRequest.OutputDir`.
- [x] AA bundles are placed under `BuildPackageRequest.BundlesDir`.
- [x] `BuildContextKeys.OutputPath` is set to `BuildPackageRequest.OutputDir` after AA finalization.
- [x] `AAManifest.json` and `AAManifest.bin` are emitted according to `FYAssetSettings.ManifestOutputFormat`.
- [x] `HotfixPackageSizeGuard` is applied before AA manifest publication succeeds.
- [x] `AAAssetIndexBuilder` output is still embedded in `AAManifest`.
- [x] `AAAddressableBuildBackend.OrganizeOutput()` no longer copies AA output during the normal request-driven path.
- [x] `AAAddressableBuildBackend.GeneratePackageManifest()` no longer writes AA manifest files during the normal request-driven path.
- [x] No duplicate AA manifest write remains between task and backend layers.
- [x] AB build/finalization behavior remains unchanged.
- [x] `LuaScriptsIndexExporter.ExportData()` outer call in `BuildProjectManager` remains unchanged.
- [x] Any new Editor scripts are included in `Assembly-CSharp-Editor.csproj`.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out of Scope

- Adding the bootstrap tail Task (`LocalStatusExporter` migration).
- Removing `IBuildBackend.OrganizeOutput()` or `IBuildBackend.GeneratePackageManifest()`.
- Renaming existing build config assets unless approved as part of the AA/AB config-path checklist.
- Changing Addressables group movement, restore, confirm-release, or differential snapshot behavior.
- Changing AA/AB manifest schemas.
- Changing runtime hotfix loading behavior.
- Build Repository integration.
- CDN upload/push workflow.

---

## Approval Checklist

- [x] Use a separate AA `BuildPipelineConfig` asset instead of sharing the AB graph.
- [x] Keep the current `BuildPipelineConfig.asset` as the AB config for this plan, and add a new AA config path, unless a config rename is explicitly approved.
- [x] Move Addressables build, output organization, AA asset index generation, and AAManifest publication together; do not leave AA finalization half backend-owned.
- [x] Keep `IBuildBackend.OrganizeOutput()` and `GeneratePackageManifest()` for compatibility in this plan, but make AA normal path validation-only after DAG execution.
- [x] Keep AB graph/finalization unchanged in this plan.
- [x] Do not add bootstrap export to this plan; extract it as the next tail-task plan.
- [x] Keep `LuaScriptsIndexExporter.ExportData()` outer call in `BuildProjectManager` unchanged; Lua index task migration is deferred.
- [x] Run source audit plus `dotnet build XLuaHotfix.sln` after implementation; verify new Editor task files are included in the project file.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-20 | Promoted Plan-3 from AA/AB Task alignment draft into executable pending-approval plan |
| 2026-05-20 | Approved with adjustments: D3 (Lua index task) removed — exporter stays as BuildProjectManager outer call; task count reduced from 4 to 3; task breakdown renumbered T1-T8 |
| 2026-05-20 | Executed. AA Addressables build, output organization, and AAManifest publication are Task-managed; backend post methods are compatibility validation only |
| 2026-05-20 | Signed off by developer and archived |
