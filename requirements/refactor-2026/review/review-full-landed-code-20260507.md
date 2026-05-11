# Comprehensive Code Review: FYAsset Landed Refactor Code

**Review Date**: 2026-05-07
**Reviewer**: deepseekv4pro-claudecode
**Scope**: 107 C# source files, approximately 16,441 lines across 7 architectural layers
**Methodology**: Static analysis via parallel layer-specific deep review; each file read in full; cross-layer comparative analysis
**Dimensions**: Correctness, redundancy, architecture design, naming, error handling, performance, maintainability
**Processed**: 2026-05-11 · Most HIGH/MEDIUM addressed in `34e002b` + `a1aff30`. CR-1 (PackageName), CR-2 (Android ManifestLoader), CR-4 (key mismatch) remain as known debt.
**Status**: 📦 Archived (partial — 3 CR open)

---

## Executive Summary

The FYAsset refactor demonstrates a coherent architectural vision -- a custom resource management pipeline replacing Unity Addressables with a YooAsset-inspired design. The layered architecture (Configuration -> Scan -> Analyze -> Build -> Verify -> Organize -> Runtime) is well-conceived, and the DAGScheduler with Read/Write key validation is a notable strength.

However, the implementation quality is uneven. Significant code duplication exists across collector UI panels (approximately 300 lines duplicated) and path utility functions (approximately 150 lines duplicated across 3 files). Two latent correctness bugs were identified that could manifest under specific pipeline configurations. The runtime layer contains a platform blocker for Android first-time installs. Several design decisions -- such as the two-source dependency topology in the build pipeline and the dual UI-over-same-data-model pattern in the editor -- introduce maintenance hazards that will compound as the codebase grows.

The review identified 7 CRITICAL, 18 HIGH, 45 MEDIUM, and 55 LOW severity findings across 107 files.

---

## CRITICAL Findings

### CR-1: ImplicitCandidate.PackageName Never Assigned (Latent Data Integrity Bug)

**File**: `Assets/FYAsset/Scripts/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs`
**Lines**: 82, 186-191, 327

The BFS traversal creates `ImplicitCandidate` instances for discovered implicit dependencies. The `PackageName` field is never assigned -- it retains the default value of `string.Empty`. When `CreateImplicitEntry` constructs a `CollectedAssetInfo` for duplicated entries, `candidate.PackageName` is written to the entry's `PackageName` field and used as a fallback for `GroupName`. The result is that duplicated implicit dependency entries will carry `PackageName = ""` and `GroupName = ""`, breaking the three-segment bundle naming convention (`pkg_group_key`) and producing invalid bundle names downstream.

**Current probability**: Medium -- depends on whether implicit dependency sharing generates duplicated entries. The share policy must be active, and an asset must be referenced by at least `MinReferenceCount` bundles.

**Recommendation**: Assign `PackageName` during `ImplicitCandidate` construction, propagating the owning asset's package name through the BFS context.

---

### CR-2: ManifestLoader StreamingAssets Path Never Reached on Android

**File**: `Assets/FYAsset/Scripts/Runtime/Backends/AB/ManifestLoader.cs`
**Lines**: 40-66, 87

The manifest loading sequence probes four file paths sequentially: primary binary, primary JSON, StreamingAssets binary, StreamingAssets JSON. On Android, `File.Exists(path)` returns `false` for any path under `Application.streamingAssetsPath` because the APK is a compressed archive, not a filesystem directory. The code at `TryLoadFromFile` (line 87) calls `File.Exists(fallbackPath)` which always returns false on Android, so the StreamingAssets fallback path is never entered. The async HTTP-based fallback (`LoadBundleFromStreamingAssetsAsync` in ABBundleLoader) is unreachable from ManifestLoader.

**Impact**: First-time installs on Android (no hot-update available) will fail to load the manifest, leaving the resource system in an uninitialized state. Hot-update paths work correctly because they use the writable filesystem path.

**Recommendation**: In `TryLoadFromFile`, detect the Android StreamingAssets case and use `UnityWebRequest` or `UnityEngine.AndroidJNI` to read from the APK, matching the pattern already established in `ABBundleLoader.LoadBundleFromStreamingAssetsAsync`.

---

### CR-3: HandleRegistry Release Callback Silently Dropped for Null/Empty EntryId

**File**: `Assets/FYAsset/Scripts/Runtime/Models/HandleRegistry.cs`
**Lines**: 205-220

The `Release` method skips the release callback invocation when `entryId` is null or empty. The conditional block `if (!string.IsNullOrEmpty(eid))` guards both the `_entryActiveCounts` decrement and the callback invocation. If a handle is allocated without an EntryId, releasing it will clear the slot and advance the generation, but the resource cleanup callback (which decrements backend refcounts and unloads bundles) is silently suppressed. Currently all code paths pass a non-empty EntryId, but the invariant is not enforced at the API level.

**Recommendation**: Either enforce non-empty EntryId at `Alloc` time via a guard clause, or ensure the release callback is invoked regardless of EntryId presence. The current silent suppression is a future bug waiting for the first caller that omits an EntryId.

---

### CR-4: Key Mismatch in TaskVerifyBuildResult -- Latent Check Failure

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskVerifyBuildResult.cs`
**Lines**: 38-41, 73, 167-174

The `payloadKindByBundle` dictionary is keyed by `BundleBuildInfo.BundleName` (the logical bundle name, e.g. `hotfix_ui_all`). However, `NeedsUnityHeaderCheck` receives `ManifestBundleEntry.BundleName` as the lookup key, which is set to `BundleBuildInfo.OutputFileName` by `TaskGenerateManifest` (the actual output filename, e.g. `hotfix_ui_all_md5hash.bundle`). When the `BundleName_HashName` filename style is active, these keys differ, causing the dictionary lookup to fail silently. The fallback `return true` (line 171) means all bundles are checked for UnityFS headers, which is the correct default behavior. However, the intended optimization of skipping RawFile header checks is non-functional for hash-styled bundle names. The code works coincidentally for the plain `BundleName` style.

**Recommendation**: Key the `payloadKindByBundle` dictionary by the output filename (matching `ManifestBundleEntry.BundleName`), or pass the `PayloadKind` directly through the manifest entry rather than maintaining a separate lookup.

---

### CR-5: Massive Code Duplication -- CollectorPanel vs CollectorSettingPanel

**Files**: `CollectorSettingPanel.cs` (721 lines), `CollectorPanel.cs` (733 lines)

Both files edit the same `CollectorSetting` ScriptableObject but with different UI layouts. The following methods are near-identical copies:

| Method | Lines Duplicated |
|--------|-----------------|
| `DrawCollectorRow` | ~50 lines each |
| `AddCollector` | ~20 lines each |
| `RemoveCollector` | ~7 lines each |
| `PickCollectPath` | ~15 lines each |
| `DrawRulePopupField` | ~14 lines each |
| `LoadSetting` | ~8 lines each |
| `CreateSetting` | ~10 lines each |
| `ApplyChanges` | ~12 lines each |
| `GetPackageNames` | ~15 lines each |
| `GetGroupNames` | ~15 lines each |

Approximately 300 lines of duplicated editor logic. Any change to collector editing behavior must be applied identically in both files.

**Recommendation**: Extract shared operations into a `CollectorEditorUtility` static class, leaving only layout-specific rendering in each panel.

---

### CR-6: Path Utility Functions Duplicated Across Three Files

**Files**: `CollectionScanner.cs`, `CollectorReverseIndex.cs`, `CollectorSettingValidator.cs`

Seven utility functions are duplicated verbatim:

| Function | Lines | In Files |
|----------|-------|----------|
| `NormalizePath` | 7 | CollectionScanner, CollectorReverseIndex, CollectorSettingValidator |
| `MatchesIgnorePattern` | 50 | CollectionScanner, CollectorReverseIndex |
| `ContainsPathSegment` | 23 | CollectionScanner, CollectorReverseIndex |
| `PathDepth` | 15 | CollectionScanner, CollectorReverseIndex |
| `IsPathContained` | 12 | CollectionScanner, CollectorSettingValidator |
| `CollectPathExists` | 9 | CollectionScanner, CollectorSettingValidator |
| `IsValidFileCollectPath` | 7 | CollectionScanner, CollectorReverseIndex |

Approximately 150 lines of duplicated logic. A bug fix in ignore-pattern matching must be manually propagated to all copies.

**Recommendation**: Extract to a shared `CollectorPathUtility` static class under `Assets/FYAsset/Scripts/Build/Collector/Editor/`.

---

### CR-7: ABBundleLoader Sync/Async Method Pair Duplication

**File**: `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs`
**Lines**: 86-242, 355-430, 464-568

Three method pairs implement identical logic for sync and async paths:
- `LoadBundle` / `LoadBundleAsync`: ~57 lines duplicated
- `LoadBundleInternal` / `LoadBundleInternalAsync`: ~52 lines duplicated
- `LoadDependenciesSync` / `LoadDependenciesAsync`: ~33 lines duplicated

Approximately 140 lines of duplicated logic. Any change to bundle loading (e.g., adding CRC verification, handling new Unity APIs, adding error codes) must be applied identically in six locations across three pairs.

**Recommendation**: Refactor to a single internal method per operation that accepts a loading strategy parameter (sync vs. async), or use a `Task`-based internal method with a sync `.Wait()` adapter.

---

## HIGH Severity Findings

### H-1: Two-Source Dependency Topology in Build Pipeline

**Files**: `BuildPipelineConfig.cs` (line 73), `DAGScheduler.cs` (line 320-343), `IBuildTask.cs`

Task dependencies can be declared in two places: `IBuildTask.DependsOn` (hardcoded in C#) and `TaskEntry.DependsOn` (configured in the ScriptableObject). The scheduler merges both sources in `GetMergedDependencies`, but there is no validation that the two sources agree. A developer updating `IBuildTask.DependsOn` without updating the SO (or vice versa) will produce an incorrect dependency graph with no compile-time or validation-time warning, only surfacing as a runtime `MISSING_DEPENDENCY` error during build.

**Recommendation**: The SO `DependsOn` should be additive only (extra dependencies not declared in code), and the validator should warn on code-declared dependencies that are duplicated or contradict the SO definition. Alternatively, deprecate the SO-level dependency field entirely.

---

### H-2: Index-Building Logic Duplicated Between ABManifest and ABAssetIndex

**Files**: `ABManifest.cs` (lines 73-177), `ABAssetIndex.cs` (lines 54-124)

Both classes build four identical index structures (`_addressIndex`, `_entryIdIndex`, `_typeIndex`, `_labelIndex`) from the same source data. The loop structure, null checks, and dictionary construction are near-identical across approximately 70 lines. The indices serve different consumers (ABManifest for build-time queries, ABAssetIndex for runtime queries), but construction logic is duplicated rather than shared.

**Recommendation**: Have `ABAssetIndex.BuildIndex` accept a pre-built index from `ABManifest.Initialize`, translating `ManifestAssetEntry` indices to `RuntimeAssetEntry` indices rather than rebuilding from scratch. Or merge the two classes.

---

### H-3: AssetPackageManager Type-Cast Coupling to ABPackageBackend

**File**: `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs`
**Lines**: 362, 371

The `LoadResolvedAsync` and `LoadResolvedSync` methods cast `_backend` to `ABPackageBackend` to access AB-specific internal methods (`LoadAssetTupleAsync`). This breaks the `IPackageBackend` interface abstraction -- adding a third backend implementation (e.g., a network-based backend) would require modifying the manager to add another type-cast branch.

**Recommendation**: Either expose the bundle name through `IPackageBackend` (adding a method or extending the return type), or store the bundle-to-asset mapping in the backend's own cache and look it up on release.

---

### H-4: CollectorAssetInspectorGUI Loads SO From Disk Every Frame

**File**: `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorAssetInspectorGUI.cs`
**Lines**: 86

Every helper method (`GetCollector`, `GetPackageName`, `GetGroupName`, `RemoveCollector`) calls `LoadSetting()`, which loads the `CollectorSetting` ScriptableObject from disk via `AssetDatabase.LoadAssetAtPath` and runs the data migrator. This fires on every `OnHeaderGUI` call, which executes on every Inspector repaint. For large projects, this is a significant performance regression in the Unity Editor.

**Recommendation**: Cache `_setting` as a static field and invalidate only when the asset is modified (via `AssetPostprocessor` or `Undo.undoRedoPerformed`).

---

### H-5: LegacyHotfixBackend Uses Non-Atomic File Write

**File**: `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs`
**Lines**: 128, 147

`PostDownloadAsync` writes `version_state.json` using `File.WriteAllText` directly. If the process crashes mid-write, the file is corrupted on next launch. The `ABHotfixBackend` counterpart uses `FileHelper.WriteAllBytesAtomic` for the same operation, which writes to a temp file and atomically renames.

**Recommendation**: Replace `File.WriteAllText` with `FileHelper.WriteAllTextAtomic`, matching the AB backend pattern.

---

### H-6: All Resources Assets Unconditionally Classified as Serialized

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskCollectBuiltins.cs`
**Lines**: 98-99

Assets discovered under `Assets/Resources` are unconditionally assigned `EPayloadKind.Serialized`. If a `.unity` scene file exists under Resources, it will be marked Serialized and packed into a non-scene AssetBundle, which Unity does not support. The classifier should check for scene files.

**Recommendation**: Apply `AssetClassifier.InferPayloadKind` or check file extension before assigning `PayloadKind`.

---

### H-7: TaskPrepareContext GetCommandLineArg Lacks Bounds Check

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskPrepareContext.cs`
**Lines**: 69

The method `GetCommandLineArg` accesses `args[i + 1]` without verifying `i + 1 < args.Length`. If a CLI flag is the last argument (e.g., `--backend` with no value), the code throws `IndexOutOfRangeException`. Malformed CLI input should produce a user-friendly error message.

**Recommendation**: Add bounds check and return a descriptive error message.

---

### H-8: BuildMessage CrossPackageContainment Reuses Error Code

**File**: `Assets/FYAsset/Scripts/Build/Editor/BuildMessage.cs`
**Lines**: 146

`CrossPackageContainment` reuses the error code `BuildErrorCodes.CrossPackageOverlap`. A containment relationship (directory A contains directory B) is semantically different from simple path overlap. Reusing the same error code makes aggregate error reporting ambiguous.

**Recommendation**: Add a dedicated `CROSS_PACKAGE_CONTAINMENT` error code constant.

---

### H-9: TaskBuildBundles Double-Iteration Over Asset Groups

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskBuildBundles.cs`
**Lines**: 65-108, 134-158

The asset grouping loop (lines 65-108) already separates assets into serialized, scene, and raw lists. A second loop (lines 134-158) re-iterates all groups to build separate `groupSerializedPaths` and `groupScenePaths` dictionaries, duplicating the work already performed. This adds O(n) overhead for every asset in the build.

**Recommendation**: Capture the per-group path lists during the first iteration pass.

---

### H-10: ManifestAssetEntry and RuntimeAssetEntry Field Duplication

**Files**: `ManifestAssetEntry.cs`, `RuntimeAssetEntry.cs`

Every field except `BundleIndex` is duplicated between the two classes. The `ToRuntimeEntry()` method manually copies 8 fields. Adding a field to one class requires adding it to both, or the mapping silently omits it. No interface or base class enforces the contract.

**Recommendation**: Define a shared data contract (interface or base class) that both types implement, ensuring compile-time alignment.

---

### H-11: HotfixManager Lacks CancellationToken Support

**File**: `Assets/FYAsset/Scripts/LegacyRuntime/HotfixManager.cs`
**Lines**: `InitializeAsync`

The hotfix pipeline initialization accepts no `CancellationToken`. If the application quits during a hotfix step (particularly during network downloads), there is no mechanism to abort gracefully. The Unity MonoBehaviour lifecycle integration for cancellation is absent.

**Recommendation**: Add a `CancellationToken` parameter to `InitializeAsync`, and propagate cancellation checks through the download and file write steps.

---

### H-12: NetworkDownloader Triple Code Duplication

**File**: `Assets/FYAsset/Scripts/Helpers/NetworkDownloader.cs`

`DownloadFile` (77 lines), `DownloadText`, and `DownloadBytes` share approximately 95% identical retry loop structure. The only differences are the download handler type and the post-download processing.

**Recommendation**: Extract to a single parameterized method (`Download<T>(string url, Func<UnityWebRequest, T> resultExtractor)`) with specific overloads.

---

### H-13: Editor Directory Filtering Implemented in Three Places

**Files**: `CollectAll.cs` (lines 51-74), `DependencyAnalyzer.cs` (lines 362-366), `CollectionScanner.cs`

The check for Editor directory paths (`/Editor/`, `\Editor\`) is independently implemented in three different files. If a new Unity-special directory is added (e.g., `Plugins/` with special packaging rules), all three locations must be updated.

**Recommendation**: Consolidate into a single `SystemIdentifiers.IsEditorDirectory(string path)` or equivalent shared utility.

---

### H-14: ManifestLoader Sequential I/O for File Probing

**File**: `Assets/FYAsset/Scripts/Runtime/Backends/AB/ManifestLoader.cs`
**Lines**: 40-66

Four file existence checks and reads are performed sequentially (primary bin, primary JSON, fallback bin, fallback JSON). Each blocks on I/O before trying the next. On slow storage or network drives, this serial I/O adds startup latency. The primary binary and primary JSON could be probed in parallel before falling back.

**Recommendation**: Probe primary bin and primary JSON paths concurrently, only falling back to StreamingAssets sequentially if both fail.

---

### H-15: TaskVerifyBuildResult Reads Bundle Files Twice

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskVerifyBuildResult.cs`
**Lines**: 100

`HashGenerator.GenerateFileHash` reads the entire bundle file from disk. This is a duplicate of the hash computation already performed in `TaskBuildBundles`. For a 500MB bundle, the file is read twice in the same pipeline run, doubling verification I/O.

**Recommendation**: Store the pre-computed hash in `BundleBuildInfo` and compare against that value rather than re-reading the file.

---

### H-16: AddressablesBackend Missing UnloadByEntryId Implementation

**File**: `Assets/FYAsset/Scripts/Runtime/Backends/Addressables/AddressablesBackend.cs`

`AssetPackageManager.CreateLegacyHandle` registers a release callback that calls `_backend.UnloadByEntryId(releaseEntryId)`. The `AddressablesBackend` does not have a `UnloadByEntryId` method distinct from `UnloadAsset(string key)`. If the `IPackageBackend` interface declares `UnloadByEntryId` with a default no-op implementation, the legacy backend's `ResourceEntry.ReferenceCount` is never decremented during Handle-based release, causing a reference count leak. If it does not declare this method, a compilation error would occur. Either case represents an integration gap in the legacy handle path.

**Recommendation**: Implement proper `UnloadByEntryId` support in `AddressablesBackend`, or ensure the legacy handle path uses key-based unloading consistently.

---

### H-17: Scheduler Deadlock Detection Code is Unreachable

**File**: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/DAGScheduler.cs`
**Lines**: 231-236

After `ValidateInternal` successfully completes Kahn topological sort, `indegree` cannot contain a non-zero node without a cycle. The `SCHEDULER_DEADLOCK` branch in `ExecuteInternal` can never be reached in practice, making it dead code. This creates a false sense of safety regarding runtime deadlock detection -- the guard appears to provide protection that the validation phase already guarantees.

**Recommendation**: Either remove the dead branch or add a comment documenting its redundancy as a defense-in-depth measure.

---

### H-18: ABPackageBackend Uses TaskCompletionSource&lt;object&gt; with Wasted Allocation

**File**: `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs`
**Lines**: 328, 405

The inflight load deduplication uses `TaskCompletionSource&lt;object&gt;` where the result object is never read (`myTcs.TrySetResult(null)`). This allocates a reference-type result slot that is never consumed.

**Recommendation**: Use `TaskCompletionSource&lt;bool&gt;` or a dedicated value type to reduce memory pressure.

---

## MEDIUM Severity Findings (Representative Sample)

A detailed listing of all 45 MEDIUM findings is recorded in the per-agent reports. The following are representative of systemic patterns:

### Architecture / Design

| ID | File | Finding |
|----|------|---------|
| M-1 | `CollectionScanner.cs:431` | `TryCollectAsset` accepts 10 parameters; method has excessive responsibilities |
| M-2 | `AssetClassifier.cs` | `MapRole` has no case for `ECollectorType.Implicit`, throwing at runtime if called |
| M-3 | `BundleDependencyGraph.cs` | `Edges` is a public mutable List; external code can bypass cache invalidation |
| M-4 | `BuildContext.cs:20` | `Get<T>` returns `default(T)` for missing keys; indistinguishable from "set to null/0" |
| M-5 | `CollectorReverseIndex.cs` | Singleton creates bidirectional coupling with `CollectorDataMigrator` |
| M-6 | `RuleResolver.cs` | Instantiates every rule just to verify it exists; should expose `CanResolveType` |
| M-7 | `CollectorReverseIndex.cs` | Undo event subscription in constructor; leak on non-standard domain reloads |
| M-8 | `IAssetIndex.cs:32` | Default interface methods throw `NotSupportedException`; Liskov Substitution violation |
| M-9 | `TaskPrepareContext.cs:41` | Version fallback uses `DateTime.Now` (local time); should use `DateTime.UtcNow` |
| M-10 | `AssetConflictRules.cs:171` | `CheckLabelSubsetAmbiguity` has O(n^2 * m^2) worst-case complexity |
| M-11 | `AssetAddressGenerator.cs:66` | `ParseTypeSuffixAddress` may misclassify names with natural underscore suffixes |
| M-12 | `TaskBuildBundles.cs:32` | `BuildPipelineConfig` loaded from disk redundantly; already loaded in `TaskPrepareContext` |

### Duplication / Redundancy

| ID | Files | Finding |
|----|-------|---------|
| M-13 | `PipelinePanel.cs`, `VersionPanel.cs` | Both follow identical embedded-SO-inspector pattern (~60 lines duplicated) |
| M-14 | 4 UI files | `NormalizePath` duplicated in 4 files (CollectorPanel, CollectorContextMenu, CollectorAssetInspectorGUI, CollectorTargetPickerPopup) |
| M-15 | 4 UI files | `LoadSetting` duplicated in 4 files |
| M-16 | 3 files | Collector initialization block (10 property assignments) duplicated in 3 files |
| M-17 | `AssetConflictRules.cs:219` | `GetNormalizedLabelSetKey` creates new sorted string per call; should be cached |

### Error Handling

| ID | File | Finding |
|----|------|---------|
| M-18 | `ABPackageBackend.cs:373` | Sync path does not catch `LoadAsset` exceptions; async path does |
| M-19 | `TaskOrganizeOutput.cs:43` | Bundle copy loop has no try-catch; disk-full error produces generic failure |
| M-20 | `ABBundleLoader.cs:447` | `AssetBundleCreateRequestToTask` does not distinguish corrupt-file from not-found |
| M-21 | `BuildTaskResolver.cs:40` | Bare `catch` swallows unexpected exceptions including `StackOverflowException` |
| M-22 | `HotfixManager.cs:442` | `FinishHotfix` calls async initialization without `await` |
| M-23 | `PackageCleaner.cs:128` | `ClearAllHotfix` calls `Caching.ClearCache()`; deprecated in Unity 2022+ |

### Naming / Documentation

| ID | File | Finding |
|----|------|---------|
| M-24 | `ScriptObjectDataBse.cs` | Typo in class name: "DataBse" should be "DataBase" |
| M-25 | `FYAssetConstants.cs:52` | Typo in constant: "DEAULT_XLUA_TYPE_CONFIG" should be "DEFAULT_..." |
| M-26 | `BuildContextKeys.cs:12` | `SharePolicies` key uses inconsistent spelling (missing 'd' compared to `SharedGroupName`) |
| M-27 | `RuntimeAssetEntry.cs:80` | `GetNormalizedLabels()` does not actually normalize; uses case-insensitive comparer |
| M-28 | `ResolveResult.cs` | `ResolveStatus.Conflict` vs `RuntimeErrorCodes.AmbiguousMatch` -- same concept, two names |
| M-29 | `ABManifest.cs:45` | Stale TODO comment: "明确哪些唯一映射" -- already resolved in code |
| M-30 | `ABPackageBackend.cs:271` | "Tuple" suffix on method names is non-descriptive of method purpose |
| M-31 | `RuntimeMessage.cs:55` | `[Serializable]` attribute on class with no parameterless constructor; misleading contract |
| M-32 | `HandleRegistry.cs:16` | Thread-safety claim ("Interlocked 原子操作即可") is incorrect for the data structures used |

---

## Architecture Assessment

### Strengths

1. **Pipeline DAG Design**: The DAGScheduler with Kahn topological sort, Write-Write conflict detection, and Read-before-Write warning is a textbook implementation of defensive data-flow validation. The four-phase validation (MissingDep -> Circular -> W-W -> R-before-W) correctly orders by severity.

2. **Layered Separation**: The clear separation between configuration (CollectorSetting SO), scan (CollectionScanner), analysis (DependencyAnalyzer), build (TaskBuildBundles), verification (TaskVerifyBuildResult), and organization (TaskOrganizeOutput) is well-defined. Each layer's output is typed and traceable through BuildContext keys.

3. **Error Model Consistency**: BuildMessage and RuntimeMessage use a consistent pattern: string error codes with factory methods that encapsulate message formatting. The separation between Editor and Runtime error types is architecturally sound.

4. **Interface-Backend Pattern**: The consistent use of interface-backend separation (IAssetIndex, IPackageBackend, IHotfixPipeline) across the runtime layer enables the dual Legacy/AB backend strategy. This pattern is applied uniformly and correctly.

5. **Handler Lifecycle Model**: The AssetHandle struct with generation-based invalidation and HandleRegistry provides a zero-GC ownership model for loaded assets. The design is clean and well-isolated.

6. **SharePolicy Decision Matrix**: The DependencyAnalyzer's share policy (ForceShare/NoShare/MinReferenceCount/MinAssetSizeBytes) with explicit conflict reporting (ForceShare in NoShare -> Error) is well-designed.

### Structural Concerns

1. **Dual UI Over Same Data Model**: `CollectorSettingPanel` and `CollectorPanel` both provide full editing capabilities for the same ScriptableObject, duplicating approximately 300 lines of logic. This is the single largest maintainability liability in the editor layer.

2. **No Shared Path Utility Layer**: Path normalization, ignore-pattern matching, segment checking, and depth computation are duplicated across three editor files. A shared utility class is the highest-impact low-effort improvement available.

3. **No Namespace Usage**: All 107 files reside in the global namespace. As the codebase grows, name collisions become inevitable. Types like `BuildResult`, `BuildContext`, and `BuildMessage` are likely to conflict with other modules.

4. **Public Mutable Fields**: All data model classes use public fields, consistent with Unity serialization requirements but inconsistent with encapsulation principles. No validation occurs at mutation points.

5. **Hardcoded Asset Paths**: Critical ScriptableObject paths (`BuildPipelineConfig`, `VersionDataBase`) are hardcoded in multiple task files, preventing multi-project reuse. These should be injectable via `BuildContext` or a configuration service.

6. **No Automated Test Coverage**: The DAGScheduler, BuildTaskResolver, all seven backbone tasks, and the HandleRegistry have no dedicated unit tests. Verification is exclusively via `dotnet build` success, which provides zero behavioral validation.

---

## Redundancy Heatmap

The following table quantifies the most significant duplication hotspots across the codebase.

| Duplication | Files | Approx. Lines | Risk |
|-------------|-------|---------------|------|
| CollectorPanel vs. CollectorSettingPanel editor logic | 2 | 300 | HIGH -- any collector editing change requires two-file sync |
| Path utilities (NormalizePath, MatchesIgnorePattern, etc.) | 3 | 150 | HIGH -- bug fix in pattern matching requires three-file sync |
| ABBundleLoader sync/async method pairs | 1 | 140 | HIGH -- any loading logic change needs 6 locations updated |
| ABManifest vs. ABAssetIndex index building | 2 | 70 | MEDIUM |
| PipelinePanel vs. VersionPanel SO inspector pattern | 2 | 60 | LOW |
| Collector init block (10 property assignments) | 3 | 30 | MEDIUM |
| NetworkDownloader retry loop | 1 | 60 | MEDIUM |
| Editor directory filtering | 3 | 20 | LOW |

---

## Risk Assessment

### Immediate Action Recommended (correctness)

1. CR-4: Fix key mismatch in `TaskVerifyBuildResult` -- latent bug, triggers under hash-suffixed bundle naming
2. CR-2: Fix Android StreamingAssets manifest loading -- platform blocker for first-time installs
3. CR-1: Fix `ImplicitCandidate.PackageName` assignment -- latent data integrity bug in implicit dependency handling
4. CR-3: Enforce non-null EntryId in `HandleRegistry.Alloc` or fix callback suppression

### This Iteration Recommended (maintainability)

5. CR-5: Extract shared collector editing operations from duplicated panels
6. CR-6: Extract shared path utilities to a single utility class
7. CR-7: Unify sync/async method pairs in ABBundleLoader
8. H-1: Resolve two-source dependency topology ambiguity

### Next Iteration Recommended (design)

9. H-3: Remove AssetPackageManager type-cast coupling to ABPackageBackend
10. H-4: Fix CollectorAssetInspectorGUI per-frame SO loading
11. H-10: Define shared data contract for ManifestAssetEntry/RuntimeAssetEntry field alignment
12. M-8: Fix IAssetIndex default method LSP violation

### Deferred (non-blocking)

13. Namespace migration for all 107 files
14. Public field to property migration for data model classes
15. Automated test suite for pipeline core and tasks

---

## Conclusion

The FYAsset refactor is architecturally sound and directionally correct. The pipeline DAG design, interface-backend separation, and Handle lifecycle model are well-executed. The primary quality concerns are not in the architecture but in the implementation details: duplicated code, latent bugs in edge-case handling, and maintenance hazards from unenforced invariants.

The seven critical findings should be addressed before the pipeline enters production use. The redundancy hotspots -- particularly the duplicated editor panels and path utilities -- represent the highest-leverage cleanup opportunities for reducing future maintenance burden.

The codebase would benefit from establishing and enforcing three conventions going forward: (a) shared utility extraction for any logic appearing in more than one file, (b) consistent error handling patterns across sync and async code paths, and (c) automated test coverage for pipeline invariants (cycle detection, key validation, bundle output correctness).

---

*End of Report*
