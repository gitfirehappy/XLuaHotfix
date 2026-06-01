# FYAsset Full Review

> **Date**: 2026-05-31
> **Reviewer**: Codex
> **Scope**: FYAsset build pipeline, Collector integration, AB/AA backend selection, AB manifest/runtime/hotfix flow, repository preview, and relevant plan/progress/git traceability
> **Method**: Static code review, workflow simulation, configuration review, historical plan/progress/review trace, and git log trace

## Findings

### P0: Legacy W-W validation and comments conflict with the current single-thread data-dependency model

The current architecture has already moved away from treating `WriteKeys` overlap as a parallel write hazard. Unity Editor execution is single-threaded and deterministic: `DAGScheduler` executes ready tasks in lexical order on the main thread (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:260`), and the dependency graph should now express simple data ordering, not mutual-exclusion ownership.

However, legacy W-W validation still remains in `DAGScheduler.ValidateInternal()`. It builds a single `writeOwners` map and fails with `CONFLICTING_WRITE_KEYS` whenever a second enabled task declares the same key (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:162`, `:170`). This stale rule conflicts with the current `CollectedAssets` augmentation flow:

- `TaskCollectAssets.WriteKeys = CollectedAssets, SharePolicies` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskCollectAssets.cs:12`)
- `TaskAnalyzeDependencies.WriteKeys = CollectedAssets, BundleDependencyGraph` (`Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/TaskAnalyzeDependencies.cs:15`)
- `TaskCollectBuiltins.WriteKeys = CollectedAssets` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskCollectBuiltins.cs:18`)

The default `Assets/Build/BuildPipelineConfig.asset` enables all three (`Assets/Build/BuildPipelineConfig.asset:22`, `:25`, `:28`). Under the old validator, this valid single-thread staged data flow can be rejected before execution.

Traceability shows why this is stale: early E5-1 docs still describe W-W conflict detection (`requirements/plan/archive/plan-E5-1.md:134`, `:157`), but the same plan already allowed same-key `CollectedAssets` augmentation (`requirements/plan/archive/plan-E5-1.md:136`, `:270`). Later project records clarify execution as "logical DAG + single-thread serial execution" (`requirements/plan/drafts/archive/draft-framework-comparison.md:38`) and `requirements/plan.md` records that "batch" means topological grouping executed sequentially on the Unity main thread (`requirements/plan.md:200`). Current verified architecture also treats task graph assets as the backbone source of truth and `BuildProjectManager.CreateBackend()` as the centralized backend selection point (`context/architecture/resource-build-and-release.md`). The current scheduler validator, `BuildConfig` comment, `BackendMode` comment, and human docs did not fully follow that semantic shift.

Impact: AB full build, AB hotfix build, and AB diff preview can be falsely blocked by a retired conflict model. This is not a real concurrent write safety issue; it is legacy validation and documentation drift.

Recommended fix: remove W-W as a fatal validation concept from the active scheduler and editor graph tooling, or at minimum downgrade it to a non-blocking diagnostic for suspicious graph authoring. Keep the hard checks focused on task existence, cycles, and data ordering. Update `ValidatePair`, `DAGScheduler` XML comments, `BuildConfig`, `BackendMode`, and human docs so `ReadKeys` / `WriteKeys` mean data dependency declarations, not write-lock ownership.

Related UI drift: E12 explicitly records that data-flow edges are derived display-only relationships and do not change scheduler order (`requirements/plan/archive/plan-E12-buildgraph-editor.md:65`, `:189`). `BuildGraphView.CreateDataFlowEdges()` still builds a `WriteKey -> single producer` dictionary (`Assets/FYAsset/Scripts/Build/Editor/ABPipeline/BuildGraph/BuildGraphView.cs:352`), so staged writes to the same key only keep the last writer. This is not a scheduling bug, but it can render misleading `CollectedAssets` data-flow edges after W-W validation is corrected. The graph should either render all producers/consumers for a key or label staged writes explicitly.

### P1: `TaskCollectBuiltins` runs after dependency analysis in the actual topological order

`TaskCollectBuiltins` says it runs before `TaskAnalyzeDependencies` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskCollectBuiltins.cs:11`), and E5-2 planned it as `RunsBefore: [TaskAnalyzeDependencies]` (`requirements/plan/archive/plan-E5-2.md:113`). The implemented dependency does not express that. Both `TaskCollectBuiltins` and `TaskAnalyzeDependencies` depend only on `TaskCollectAssets` (`TaskCollectBuiltins.cs:16`, `TaskAnalyzeDependencies.cs:13`).

`DAGScheduler` sorts ready tasks alphabetically (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:260`). After `TaskCollectAssets`, both tasks are ready; `TaskAnalyzeDependencies` sorts before `TaskCollectBuiltins`. The simulated execution order is:

1. `TaskPrepareContext`
2. `TaskCollectAssets`
3. `TaskAnalyzeDependencies`
4. `TaskCollectBuiltins`
5. `TaskBuildBundles`

Impact: built-in shader/resources entries are appended after dependency analysis. `TaskBuildBundles` consumes the augmented `CollectedAssets`, but `BundleDependencyGraph` was computed without those appended assets. Manifest dependency edges can therefore be incomplete for bundles introduced by `TaskCollectBuiltins`.

Recommended fix: express the intended order directly in the simple data-dependency graph. Make `TaskAnalyzeDependencies.DependsOn` include `TaskCollectBuiltins`, or add the equivalent SO dependency line, so the existing `CollectedAssets` staged update remains the single data flow.

### P2: Scene bundle manifest mapping relies on the current `PackSeparately + short GUID` invariant

`TaskBuildBundles` creates separate Unity bundle outputs for scenes using `bundleName + "_scene_" + s` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:109`). When collecting results, each scene output is represented as a `BundleBuildInfo` whose `BundleName` is the original logical name and `OutputFileName` is the physical scene file (`TaskBuildBundles.cs:188`).

`TaskGenerateManifest` builds `bundleNameToIndex` using `buildResults[i].BundleName` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:35`). If one logical bundle contains multiple scene outputs, later entries overwrite earlier entries. Asset entries are then mapped by `a.BundleName` (`TaskGenerateManifest.cs:72`), so all assets sharing the logical scene bundle name point to the last scene output index.

This is not a confirmed current-path bug: the scanner forces scene assets to `PackSeparately` (`Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:659`), and `BundleNameBuilder` includes the short GUID in the `PackSeparately` bundle key (`Assets/FYAsset/Scripts/Build/Collector/Editor/BundleNameBuilder.cs:87`). E5-2a also recorded the scene output collapse fix as resolved (`requirements/review/archive/review-e5-2a-20260507.md:22`). Under those invariants, normal scan output should not create multiple scenes sharing one logical bundle name.

Residual risk: the manifest generation contract still mixes logical and physical bundle identity. If a future migration, manual metadata path, or scene grouping change violates the one-scene-per-logical-bundle invariant, the current dictionary overwrite will fail silently.

Recommended fix: keep this as a defensive validation, not a primary bug fix. During `TaskGenerateManifest`, detect duplicate `BundleBuildInfo.BundleName` entries when they point to different `OutputFileName` values and fail with a clear invariant error, unless the manifest assignment is changed to use an explicit asset-path-to-output mapping.

### P2: `BackendMode` comments/docs still describe an older CLI and W-W authority model

The user-flagged comment is stale:

```csharp
/// 实际来源为 FYAssetSettings.Instance.UseABBackend，CLI --backend 可局部覆盖。
/// DAG W-W 冲突检测保证单一 Task 独占写入此 Key。
```

This appears in `Assets/FYAsset/Scripts/Build/BackendMode.cs:3`. The second line is no longer true: `BuildContextKeys` does not define a `BackendMode` key, and `TaskPrepareContext` only writes `BuildConfig` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:16`).

The first line reflects an older design more than the current release path. `TaskPrepareContext` still parses `--backend` and puts it into `BuildConfig` (`TaskPrepareContext.cs:24`), but the formal release flow creates `BuildPackageRequest` before the DAG using `FYAssetSettings.Instance.UseABBackend` (`Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildProjectManager.cs:97`), selects the backend with the same settings value (`BuildProjectManager.cs:127`), and package metadata tasks later consume `BuildPackageRequest.BackendMode` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskWritePackageIndex.cs:25`). `BuildCommandLine` documents `-backend` as repository CLI-only, and `BuildRepositoryCLI` has its own `-backend` parameter for status/diff/push/list operations (`Assets/FYAsset/Scripts/Build/Repository/Editor/CLI/BuildRepositoryCLI.cs:88`).

Historical trace explains the drift: E5-1 originally approved `BackendMode` as a context key protected by DAG W-W (`requirements/plan/archive/plan-E5-1.md:144`, `:156`), E11 later moved backend ownership to `FYAssetSettings` (`requirements/progress.txt:882`, `:899`), Build Request ownership moved final package identity into `BuildPackageRequest` (`requirements/progress.txt:1067`), and backend metadata work added `PackageIndex`/`BuildIndexData` backend fields (`requirements/progress.txt:1167`). The enum comment and docs were not fully updated after those ownership changes.

Impact: comments/docs and code now describe different control planes. A developer may expect `--backend` to switch formal release builds, while current official builds use `FYAssetSettings.UseABBackend`; repository CLI `-backend` is a separate repository/diff selection parameter.

Recommended fix: remove the W-W key claim. Treat official release backend selection as `FYAssetSettings.UseABBackend` unless a new plan explicitly reintroduces a release CLI override; update `BackendMode`, `TaskPrepareContext`, `BuildCommandLine`, and docs accordingly. If release CLI override is desired later, it must be parsed before `BuildPackageRequest.Create()` and `CreateBackend()`, not only inside `TaskPrepareContext`.

### P2: Preview whitelist does not isolate validation from unrelated full-config errors

`RepositoryPreviewRunner` builds task whitelists for preview (`Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:61`). But `DAGScheduler.Execute(... taskWhitelist)` validates the full enabled `BuildPipelineConfig` before filtering tasks (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs:43`), while whitelist filtering happens only inside `ExecuteInternal` (`DAGScheduler.cs:238`).

Traceability: BRC-2 intentionally chose stop-after/whitelist for preview and explicitly rejected a separate preview graph (`requirements/plan/archive/plan-build-repository-core-20260523.md:27`, `:142`). The plan does not say that preview should first validate unrelated non-whitelisted tail tasks. Current code also already follows the later IP-44 fix by passing the AB preview output root through `BuildContextKeys.RepositoryPreviewOutput`, not an environment variable (`context/mistakes/implementation-pitfalls.md:308`, `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:60`).

Impact: preview can fail due to unrelated disabled-in-preview problems in the full graph. The stale W-W validator is the current concrete example: AB preview asks to stop at `TaskScanABHotfixDiff`, but full-config validation can reject valid staged writes before preview execution starts.

Recommended fix: run validation on the effective task set, or split validation into full-graph and scoped-graph modes. Preview should validate only the tasks it will execute plus their required dependencies.

### P3: `BuildConfig.OutputRoot` is now a temp-build root, but the name/comment still read like final output authority

`TaskPrepareContext` computes `BuildConfig.OutputRoot` from `--output`, preview override, or default build path (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs:64`). `BuildPackageRequest.Create()` separately computes the final output directory from `BuildPathManager.GetPackageDir()` (`Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildPackageRequest.cs:41`).

This split is an approved later design, not a bug. BOU-1 made `BuildPackageRequest` the owner of final package identity and output paths (`requirements/plan/archive/plan-build-request-output-ownership-20260520.md:35`, `:56`), and ABF-1 moved AB finalization under `BuildPackageRequest.OutputDir` / `BundlesDir` (`requirements/plan/archive/plan-ab-finalization-task-20260520.md:35`). Current AB tasks use `BuildConfig.OutputRoot` for temporary `_temp` output (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:33`, `TaskOrganizeOutput.cs:29`) and `BuildPackageRequest.OutputDir`/`BundlesDir` for final output (`TaskOrganizeOutput.cs:34`, `:35`).

Residual issue: the name/comment still make `OutputRoot` look like a final output authority. A CLI `--output` value affects temporary build output, not final package location, which is easy to misread without the historical plans.

Recommended fix: update comments/docs to say `BuildConfig.OutputRoot` is the temporary build root. A later rename to `TempBuildRoot` would be clearer, but the architectural ownership is already correct.

### P3: `TaskExportLocalBuildData.ReadKeys` is intentionally broad for a full-only tail task, but the conditional read is undocumented

`TaskExportLocalBuildData.ReadKeys` includes `OutputPath` (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskExportLocalBuildData.cs:18`), but `Execute()` returns before reading it when `BuildType != Full` (`TaskExportLocalBuildData.cs:35`). Static DAG analysis cannot model conditional reads, so this overdeclared key can create unnecessary ordering constraints or warnings.

BTT-1 deliberately moved local startup data export into the AA/AB graph tail, with hotfix builds skipped (`requirements/progress.txt:1098`, `:1099`). So this is not an implementation error. The only issue is contract precision: `ReadKeys` cannot express "requires `OutputPath` only for Full".

Recommended fix: document this as an accepted conditional-read limitation, or add a future task-condition mechanism if more build-type-specific tail tasks appear. Do not split the task just to satisfy the current static declaration.

### P3: AB runtime allows duplicate `ManifestBundleEntry.BundleName` silently while build assumes uniqueness

`ABManifest.Initialize()` builds `BundleName -> index` with assignment overwrite (`Assets/FYAsset/Scripts/Runtime/Manifests/AB/ABManifest.cs:142`). `TaskGenerateManifest` also builds `bundleNameToIndex` with overwrite (`Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskGenerateManifest.cs:35`). Neither path validates duplicate bundle names.

Impact: a malformed manifest with duplicate physical bundle names would silently point all runtime lookups to the last entry. This is lower severity than the scene mapping invariant because normal generated build output should already produce unique physical names; the gap is validation hardening for generated and downloaded manifests.

Recommended fix: validate unique `ManifestBundleEntry.BundleName` during manifest generation and during `ABManifest.Initialize()`. Fail build for duplicates; log or reject malformed downloaded manifests at runtime.

## Architecture and cleanup notes

- The DAG data contract is the highest-risk area. Current decisions treat the graph as single-threaded data dependency ordering, but several code comments, docs, and validator paths still describe older write-lock / W-W semantics.
- The AB backend has two identity layers for bundle data: logical bundle name and physical output file name. The split is necessary for scene outputs, but it needs to become explicit in data structures instead of inferred from naming.
- `ABBundleLoader` has largely duplicated sync/async dependency loading paths (`Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:86`, `:181`, `:463`, `:515`). This is not currently the top correctness risk, but future fixes should avoid drifting behavior between sync and async paths.
- `ABManifest` and `ABAssetIndex` both build similar address/type/label indexes (`Assets/FYAsset/Scripts/Runtime/Manifests/AB/ABManifest.cs:87`, `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:85`). The duplication is understandable for runtime query performance, but uniqueness/duplicate policies must be documented and enforced consistently.

## Decision drift table

| Area | Historical decision | Current code | Drift |
|------|---------------------|--------------|-------|
| `CollectedAssets` augmentation | Same-key read/write allowed for staged augmentation (`plan-E5-1.md:136`) and current execution is single-threaded (`draft-framework-comparison.md:38`) | DAGScheduler still has legacy fatal W-W validation | Retired conflict model can falsely block valid data flow |
| BuildGraph data-flow edges | E12 says data-flow edges are display-only and do not change scheduler order (`plan-E12-buildgraph-editor.md:65`) | BuildGraph maps each `WriteKey` to one last producer | Staged writes can render misleading producer-consumer edges |
| Builtins order | `TaskCollectBuiltins` runs before dependency analysis (`plan-E5-2.md:113`) | Both tasks depend on `TaskCollectAssets`; alphabetical order runs analyze first | Builtins not included in dependency graph |
| Backend ownership | `BackendMode` key protected by DAG W-W (`plan-E5-1.md:156`) | No `BackendMode` context key; release flow uses `FYAssetSettings` and `BuildPackageRequest` | Comment and CLI expectations stale |
| Package/backend metadata | Backend metadata added to package index/build index (`progress.txt:1167`) | Metadata uses `BuildPackageRequest.BackendMode`; `TaskPrepareContext --backend` affects only `BuildConfig` | Legacy task-local override wording must not be documented as official release authority |
| Scene output fix | Scene output collapse was recorded as fixed (`progress.txt:774`) | Current invariant is `Scene -> PackSeparately -> short GUID bundle key`; manifest still lacks duplicate logical-name guard | Defensive validation gap, not confirmed current-path bug |
| Manifest V1 fields | E6 approved `PackageName = "MainPackage"` and `AutoAddress = true` V1 (`plan-E6.md:109`, `:185`) | Code follows the approved V1 behavior | Not a bug; future evolution only |

## Recommended remediation order

1. Align DAG semantics first: remove or downgrade legacy W-W validation and update comments/docs so `WriteKeys` are data-flow declarations, not write locks.
2. Fix BuildGraph data-flow rendering so staged writes do not collapse to the last producer.
3. Fix `TaskCollectBuiltins` ordering so dependency analysis sees the exact asset set that build and manifest generation consume.
4. Remove stale `BackendMode` W-W/key comments and align docs on `FYAssetSettings.UseABBackend` as the official release backend authority.
5. Add defensive scene/duplicate-bundle validation around logical-vs-physical bundle identity.
6. Clarify `BuildConfig.OutputRoot` as temporary build root and `BuildPackageRequest` as final output authority.

## Verification status

No runtime code changes were made as part of this review. I did not run a Unity build or `dotnet build` for this report because the task was review-only and the highest-risk findings are static graph/contract inconsistencies visible before compilation.
