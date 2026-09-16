# Plan: FYAsset AB Build Chain Simplification

> **Date**: 2026-09-12
> **Status**: Partially completed — execution deviations require remediation; archived on 2026-09-16
> **Approval**: Developer approved 2026-09-12
> **Remediation**: `requirements/plan/plan-fyasset-ab-remediation-20260916.md`
> **Scope**: AB construction and delivery, Summary reuse, AB runtime loading, Shared hotfix/build contracts, and affected behavior verification

## Purpose

Implement the confirmed Content boundary and remove the redundant build, runtime, hotfix, and version state that conflicts with it.

## Constraints

- Keep AA and AB concrete implementations separate; modify Shared only for confirmed shared contracts.
- Preserve Summary-based reuse: historical successful `Build_*` packages remain the only reusable byte source.
- Do not retain compatibility fields, duplicate state, speculative abstractions, or incremental scan snapshots.
- Do not add tests unless replacing an affected source-shape test, covering an otherwise unverified confirmed contract, or explicitly requested.
- Preserve unrelated worktree changes and keep each implementation task independently reviewable.

## Success Criteria

- Each Content maps to exactly one physical file; runtime public identity is Address and internal Content references are Manifest-local indices.
- AB and AA use the same exact AssetType identifier and Labels retain only business classification.
- Bundle, asset, scene, raw-file, build reuse, hotfix baseline, version, delivery, and Summary ownership match the confirmed boundaries below.
- Existing affected source-shape tests are replaced by the narrowest behavior checks; no verification claim lacks fresh output.
- The workspace is committed only after targeted checks and `git diff --check` succeed.

## Implementation Tasks

1. **AB Content Build**: rename and reduce ContentBuildItem/ContentBuildResult, enforce one Content-one file, move dependency helpers into owning Tasks, and update build Context keys and reports.
2. **Manifest And Reuse**: reduce ManifestAssetEntry/ManifestContentEntry, remove RuntimeAssetEntry, apply AssetType/AssetPath/ContentIndex contracts, and persist ContentReuseRecord only.
3. **Runtime Loading**: implement the single Bundle owner, resolved-entry asset/scene/raw loaders, Address index, Editor-only loader, handle ownership, and scene release lifecycle.
4. **Shared Build And Hotfix**: apply BuildRequest/BuildResult names, remove the daily build sequence, narrow HotfixBaselineResolver, and remove confirmed hotfix metadata/state/event redundancy.
5. **Delivery And Verification**: apply ExportABOutputTask boundary, update AA type indexing, migrate affected tests to behavior checks, run the narrow verification set, and record evidence.

## Confirmed Terms

- **Asset**: a project asset. It may be public or implicit.
- **Content**: the minimum physical content unit shared by AB construction, historical reuse, runtime download, and runtime loading.
- **Package**: a delivered `Build_*` directory containing the complete `ABManifest` and zero or more delivered Content files.
- **Summary**: the durable record of one successfully delivered build. It is an index for later Content reuse; it is not the package itself.

A Content is not an Asset, a Bundle, or a Package.

## Confirmed Content Rules

1. One Content produces exactly one physical file.
2. One physical file belongs to exactly one Content.
3. A serialized Content produces one AssetBundle file.
4. A Scene produces one Scene AssetBundle Content.
5. A RawFile produces one RawFile Content.
6. A Content contains only build facts:
   - content identity;
   - content type;
   - member assets used as build input;
   - physical file name;
   - file Hash, CRC, and Size;
   - actual direct dependency file names.
7. Address, AssetType, Labels, public visibility, GUID, version, package name, build type, and publish target are not Content facts.

## Confirmed AB Task Chain

```text
CollectABAssets
-> AnalyzeABDependencies
-> BuildABContent
-> GenerateABManifest
-> VerifyABContent
-> ExportABOutput
```

### 1. CollectABAssetsTask

**Input**: saved `AssetCollectionSetting`.

**Output**:

- `CollectedAssets`;
- `SharePolicy`.

**Confirmed boundary**:

- Retain this as a Task.
- `CollectionScanner` is the only scanning rule implementation.
- Editor preview and build collection use `CollectionScanner`.
- Framework built-in assets must be included by `CollectionScanner`, not added as a separate `CollectABAssetsTask` patch.
- The Task loads settings, runs the scanner, stops on scan errors, and writes the result to `BuildContext`.
- The Task does not add, remove, or rewrite assets after scanning.
- A build scans again from saved configuration. The editor preview result is in-memory UI state and is not a build input.

### 2. AnalyzeABDependenciesTask

**Input**:

- frozen `CollectedAssets`;
- `SharePolicy`;
- dependency filter and RawFile rules.

**Output**:

- frozen analyzed `CollectedAssets`;
- the expected Content dependency graph.

**Confirmed rules**:

- After this Task, later Tasks and custom Tasks must not mutate `CollectedAssets`.
- A single-reference implicit dependency remains inside its referencing Content and has no independent Content.
- A multi-reference implicit dependency becomes a shared Content.
- Only explicitly collected assets have Address and may enter the public runtime index.
- Implicit dependencies have no Address and are never public runtime entries.
- The Task must consume `SharePolicy` from `BuildContext`; it must not read `AssetCollectionSetting` again.

### 3. BuildABContentTask

**Input**:

- frozen analyzed `CollectedAssets`;
- expected Content dependency graph;
- build configuration;
- historical successful Summaries and their retained `Build_*` packages.

**Output**:

- the current build's physical Content files in the temporary build directory;
- one build-output record for each Content;
- the build recipe identity.

**Confirmed boundary**:

- This Task has one purpose: obtain this build's physical Content files.
- Unity Bundle construction, Scene construction, RawFile copy, Summary lookup, historical file reuse, file digest calculation, and actual dependency collection all support that one purpose.
- Summary-based reuse remains required.
- Historical Summary dependency records are actual dependency facts exported by Unity during the prior successful build.
- Reuse must validate both content input identity and dependency validity. A historical Content can only be reused when its recorded direct dependency files remain resolvable in the current Content set, in addition to build recipe and file digest checks.
- The Task computes Hash, CRC, and Size once for each current output. Later Tasks only consume these facts.
- The Task retains the final physical file-name collision check.

**Input validation moved out of this Task**:

- Content type and AssetType consistency for a Content;
- serialized asset eligibility as an AssetBundle entry;
- RawFile one-file-per-Content rule.

These rules belong to scan and dependency analysis. The implicit RawFile content identity must be made unique before the final Task-side RawFile multiplicity guard can be removed.

### 4. GenerateABManifestTask

**Input**:

- frozen analyzed `CollectedAssets`;
- Content build outputs.

**Output**: `ABManifest`.

**Confirmed boundary**:

- Retain this as a Task.
- It maps build facts to runtime package facts only.
- It creates `ManifestAssetEntry` and `ManifestContentEntry`.
- It maps actual dependency file names to `DependencyIndices`.
- It does not scan assets, derive Unity dependencies, build or reuse Content, read temporary files, recompute Hash/CRC/Size, or inspect the expected dependency graph.

### 5. VerifyABContentTask

**Input**:

- `ABManifest`;
- Content build outputs.

**Output**: `BuildVerificationResult`.

**Confirmed boundary**:

- Validate only the mapping between the manifest and this build's Content outputs.
- Retain checks for Content output correspondence, asset `ContentIndex`, asset membership, and valid `DependencyIndices` (valid range, no duplicate, no self-reference).
- Remove repeated scan/input checks for Address, public visibility, and ContentType.
- Remove temporary-directory full scans, orphan-file checks, and Hash/CRC/Size recomputation.

### 6. ExportABOutputTask

**Confirmed boundary**:

- It receives the verified complete Manifest, current ContentBuildResults, attempt Content files, and the Hotfix baseline package root when applicable.
- Full and Standalone select all Content; Hotfix compares its complete Manifest with the baseline ABManifest and selects only added or changed Content.
- It validates the selected delivery scope's size, materializes selected files in the attempt package directory, writes the complete target ABManifest, writes the package-local BuildIndex for Full/Standalone, and creates a `CompleteBuildSummary` candidate in Context before cleaning the temporary build directory.
- Full/Standalone size validation uses the complete `Manifest.ContentEntries` total; Hotfix validation uses only the actual delivery Content total.
- It does not build Content, recompute digests/dependencies, reverify Manifest consistency, commit Summary/Index, promote, publish, switch local startup data, or roll back delivery.

## Confirmed Runner Boundary

- `HotfixBaselineResolver` is a Shared Editor read-only locator for the latest successful Full package in the exact `BackendKey + Platform + Channel` scope. It validates Summary/Index facts and package-root availability, returns only `packageRoot` and `fullBuildId`, and does not compute AA/AB diffs, mutate package files, persist build facts, cache state, or select a Hotfix baseline. Delete the coarse `BuildProjectRunner.HasFullBaseline` precheck; AA and AB Tasks use this single exact baseline entry when they need backend-specific comparison.

There are two distinct runners.

```text
BuildProjectRunner
-> ABBuildBackend
-> BuildPipelineRunner
-> AB Task chain
```

### BuildPipelineRunner

- prepares `BuildContext` through `IBuildRunEnvironment`;
- creates and discards the attempt directory;
- runs Tasks in order and stops at the first failure;
- after all Tasks succeed, promotes the attempt directory to the final package directory;
- returns a rollback-capable `IBuildDeliveryToken`.

It does not determine versions, commit Summary facts, update local startup data, or publish remotely.

### BuildProjectRunner

Before the Task chain, it:

- reads or rebuilds Summary Index;
- chooses the version;
- validates the existence of a Full baseline for Hotfix;
- creates the build request.

After the Task chain and attempt promotion, it runs the final delivery transaction:

1. apply local startup data when the build mode requires it;
2. write the permanent Summary;
3. write the Summary Index last;
4. commit the delivery token.

On failure it restores local startup data, Summary, Summary Index, and the promoted package directory.

**Confirmed decision**: retain this split. `BuildPipelineRunner` owns attempt promotion and returns the rollback token. `BuildProjectRunner` owns the final cross-system delivery commit or rollback.

## Confirmed Rename Decisions

- `BuildPackageRequest` -> `BuildRequest`.
- `BuildBackendResult` -> `BuildResult`.
- `BundleBuildInfo` -> `ContentBuildResult`, `BundleName` -> `ContentName`, and `OutputFileName` -> `FileName`.
- `ContentPlan` -> `ContentBuildItem`; it represents one Content's mutable work record exclusively inside `BuildABContentTask`, while `ContentBuildResult` represents the completed physical-output facts after that Task. A ContentBuildItem has exactly one `FileName`; delete its multi-output expression.
- `ContentBuildItem` retains only the flat `AssetPaths` and `ContentType` build inputs. Delete its `CollectedAssetInfo` member wrapper and all derived path lists; `ABBuildContentFingerprint` accepts the path collection directly.
- `ContentBuildItem.ReusedOutput` is the sole reuse-state source. Its sole `DependencyFileNames` field stores Summary-replayed direct dependencies for a reused Content or Unity-reported direct dependencies for a rebuilt Content. Delete `CacheCandidate` and `CachedDependencyFileNames`; cancelling reuse deletes its attempt artifact and clears both remaining facts.
- Rename the persisted reuse-only record `SummaryContentFact` to `ContentReuseRecord`, and `Contents` to `ContentReuseRecords` in both Summary representations. Its minimal fields are ContentName, InputFingerprint, FileName, Hash, CRC, Size, and DependencyFileNames; retain null versus empty dependencies as an invalid-record versus known-leaf distinction.

`BuildRequest` is the input for one complete build. `BuildResult` is the backend result returned to the total build orchestrator. `ContentBuildResult` is the fact record for one physical Content output. Neither name replaces `BuildTaskResult`.

## Confirmed Version Boundary

- `VersionNumber` contains only the comparable, serialized release version: Major, Minor, Patch, and Channel.
- Delete `VersionNumber.Build`, `BuildSummaryIndex.ProjectVersion.DailyBuildCount`, `ResolveTodayBuildCount`, the `BuildVersionPlanner.Plan` daily-count argument, and all Summary/index read/write paths for that cycle. These values are not part of version identity, package naming, or runtime update comparison.
- Build audit uses BuildId and start/finish timestamps; version progression policy remains in `BuildVersionPlanner`, package identity/path calculation remains in `BuildRequest`, and build-fact commit remains in `BuildProjectRunner`.

## Confirmed Runtime Identity

- Public runtime resources use `Address` as their unique identity.
- One public `Address` maps to exactly one runtime asset entry and one Content relationship.
- Runtime manifest lookup, backend cache, inflight-load deduplication, HandleRegistry ownership counts, scene records, and release callbacks use `Address`.
- Remove runtime `EntryId` fields and the runtime `EntryId -> AssetEntry` indexes.
- `AssetGUID` remains a build-time `CollectedAssetInfo` identity for Unity asset discovery and dependency analysis; it is not a runtime resource identity.
- Implicit dependencies have no runtime Entry identity. They remain build-time Content members and actual Bundle dependencies only.
- Build-time validation must reject duplicate public Addresses before Manifest export. Runtime Address lookup is a single-value mapping, not a candidate list.

## Confirmed Query Boundary

- Remove `Type + Label` combination queries from `ABAssetIndex` and `ABPackageManager`.
- Runtime infrastructure retains single-dimension Address, AssetType, and Label queries. Business code composes multiple conditions from those basic results.

## Confirmed Label Rules

- Labels are a non-empty, trimmed, case-insensitive unique set.
- Configuration validation and build scanning reject duplicate Labels, including values that differ only by casing.
- The first configured spelling is retained for display and Manifest serialization.
- `ManifestAssetEntry` stores the sole normalized Label list.
- Remove `RuntimeAssetEntry.SetLabels`, the normalized Label cache, and multi-Label matching helpers. Single-Label query results come directly from `ABAssetIndex`.


## Confirmed Hotfix Event Simplification

- Delete `ClientUpdateRequiredInfo`.
- `OnClientUpdateRequired` carries one named tuple: `(VersionNumber ClientVersion, VersionNumber RemoteVersion, string TargetPackageName)`.
- AA, AB, and compatibility facades forward that event contract unchanged.
- The event data has no behavior, persistence, serialization, or independent lifecycle; it is not a domain model.

## Confirmed Hotfix Metadata Simplification

- Delete `IHotfixPipeline.HasRequiredMetadata` and the `refreshRequiredMetadata` argument of `PersistRemoteMetadataAsync`.
- `HotfixFlowBase` only asks each backend to persist complete target metadata after Content preparation; it has no Catalog-specific branch.
- `ABHotfixBackend` always persists its cached remote ABManifest.
- `AAHotfixBackend` internally checks whether its target staging root already contains `catalog.json`, reuses it when present, and otherwise downloads it before persisting the AA Manifest.

## Confirmed Hotfix State Simplification

- Delete `HotfixContentState` completely. It is not runtime resource state and has no independent meaning beyond whether the current package root is the built-in root.
- Delete `HotfixContext.CurrentContent`, `HotfixFlowBase.CurrentContent`, `HotfixCheckResult.ContentState`, `HotfixStateDecision.ContentState`, and `HotfixFallbackDecision.ContentState`.
- Delete AA, AB, and compatibility `CurrentContent` public properties.
- `RemoteTarget` remains represented by `HotfixStateAction`, target package metadata, target staging root, and `TargetPrepared`.
- `Blocked` remains represented by a failed result, `HotfixStateAction.Block`, and the associated error.
- Activation and rollback derive the built-in/local branch from `CurrentPackageRoot` and `BuiltInPackageRoot`; no parallel content-source field is retained.

## Confirmed AB Runtime Loading Boundaries

- `ABBundleLoader` is the sole owner of all AssetBundle acquisition, actual dependency recursion, cache, inflight-load sharing, reference counting, and Bundle release.
- Rename `ABPackageBackend` to `ABAssetLoader`, `EditorPackageBackend` to `EditorAssetLoader`, and `IABLoadBackend` to `IABAssetLoader`.
- `IABAssetLoader` loads only already-resolved SerializedObject entries and maintains object-level cache and inflight state. It does not resolve Address, manage Bundle lifetime, load scenes, or read RawFiles.
- `ABSceneLoader` receives an already-resolved Scene entry and owns SceneManager lifecycle and scene Handle lifecycle; it acquires and releases Scene AssetBundles through the shared `ABBundleLoader`.
- Add a stateless `ABRawFileReader` for already-resolved RawFile entries and Content files. It reads bytes from the active package root and owns no cache, handle, or Bundle behavior.
- `ABPackageManager` resolves Address and coordinates loaders, handles, initialization, and shutdown. It is the only composition root: it constructs one shared `ABBundleLoader` and injects it into `ABAssetLoader` and `ABSceneLoader`.
- Loaders receive resolved `ManifestAssetEntry` and its Content relationship. They no longer resolve Address or perform a second Manifest lookup by runtime identity.
- Delete `ABManifestLoader`. Its binary-first/JSON-fallback read and `ABManifest.Initialize()` invocation become private `ABPackageManager` initialization helpers; it has one caller and no independent state or reuse boundary.
- Retain exactly two resource caches: `ABBundleLoader` caches acquired AssetBundles and their dependency/reference state; `ABAssetLoader` caches extracted Unity objects by Address and stores the Bundle name needed to return its acquisition. `HandleRegistry` is only Address-keyed ownership accounting, not a resource cache.
- Delete `HandleRegistry.Slot.BundleName`; it duplicates `ABAssetLoader` state and is not part of the release path.
- Keep internal two-value `HandleKind` (`Asset`, `Scene`) solely to select the release protocol and active-handle accounting. RawFile returns bytes without a Handle, cache ownership, or explicit release.
- Keep `ABSceneLoader` scene records as the source of truth for loaded Scene to Content-file ownership, Single-mode replacement settlement, and Shutdown safety. Delete `Clear()`; Shutdown refuses to run while scene records remain, rather than discarding those records without physical unload.
- Replace `SceneHandle.Release()` and `UnloadAsync()` with one `ReleaseAsync()`: non-last owners release only their token; the last owner asynchronously unloads the Scene, then settles its record and Bundle reference. On unload failure, retain all state for retry.
- Delete `activateOnLoad`, `PreloadAsync`, `PreloadWaitBudgetFrames`, and `UnloadConfirmBudgetFrames`. Scene loading returns only after activation; Single replacement waits for actual old-scene unload and never settles its record or Bundle on an arbitrary frame budget.
- Rename `EditorPackageBackend` to `EditorAssetLoader`. It is `internal sealed`, compiled only under `UNITY_EDITOR`, and created only by `ABPackageManager` Editor PlayMode initialization. It shares the resolved-entry `IABAssetLoader` contract but has no player, Bundle, hotfix, or public-backend role; `ABSceneLoader` keeps the same SceneRecord/Handle behavior in direct-path mode.
- `ManifestAssetEntry` exports public runtime assets only. Retain only Address, the asset-type query key, Labels, AssetPath, and ContentIndex; delete EntryId, IsPublic, and per-asset ContentType. Delete `RuntimeAssetEntry`; runtime indexes use `ManifestAssetEntry` directly. `ManifestContentEntry` retains all physical Content facts, including ContentType and actual dependency indices, for every Content whether or not it has a public asset mapping.
- Rename `PrimaryType` to `AssetType` across collection, build, Manifest, AA, and AB contracts. It is the common exact asset-type query key which supplements Addressables' lack of native type labels. AA and AB both derive it in the Editor from the actual main asset type (`AssetDatabase.GetMainAssetTypeAtPath`); AA stops interpreting its first Addressables Label as Type. Serialize the stable exact identifier as `assembly-simple-name:type-full-name`, such as `UnityEngine.CoreModule:UnityEngine.Texture2D`. It is not a serialized `System.Type` or an assignability hierarchy; `Object` and base-class queries do not match derived types implicitly.
- Rename `ManifestAssetEntry.SourcePath` to `AssetPath` and retain it as private loading metadata, never as a business query or public identity. `ContentIndex` locates the physical Content only; multiple public assets may map to that Content, so `AssetPath` selects the actual Unity object inside an AssetBundle and is also the Editor direct-load path. Player RawFile reads its Content file by `ContentIndex`; the retained path is relevant only to Editor direct RawFile reads.
- Retain `ContentIndex` as the compact, Manifest-local relationship from an AssetEntry to `ContentEntries`. It is valid only within the atomically generated Manifest, never an external or cross-version identity. Initialization validates every AssetEntry `ContentIndex` and every Content `DependencyIndices` item against `ContentEntries` bounds.
- Delete `CompleteBuildSummary.CollectionFingerprint` and its serialized document field. Content reuse is a per-Content optimization: `BuildRecipeFingerprint` gates compatible build recipes, and each `ContentReuseRecord.InputFingerprint` represents its own reusable Content input. The collection-wide value has no consumer and must not act as a global reuse gate.

- Delete `ContentDependencyIndexResolver`. Its reuse-dependency closure and history-dependency merge operations become private `BuildABContentTask` methods; its current-file-name index and `DependencyIndices` conversion become private `GenerateABManifestTask` methods. The source of dependency facts remains current Unity output or Summary replay only.

## Confirmed Verification Boundary

- Replace existing source-shape assertions with behavior evidence where their named source form is changed by this plan: serialized output, file layout, actual build artifacts, load/release results, scene lifecycle, and delivery transaction results.
- Do not add speculative test coverage. Add or migrate a test only when it replaces an affected existing source-shape test, when no existing test can verify an already-confirmed contract, or when explicitly requested.
- Use the narrowest applicable verification: pure logic checks for value/selection rules, Unity Editor integration for build/Manifest facts, runtime integration for loaders/scenes, and real publish/Player checks only for the affected delivery or runtime contract.

## Execution Status

Implementation, documentation alignment, and the confirmed production cleanup are complete within the approved scope. Fresh pure .NET verification passed for all 11 scenario projects: `ab_remediation` 6/6, `hotfix_flow` 8 scenarios/55 assertions, `pipeline_compose` 29/29, `pipeline_realignment` 8/8, `serialization` PASS, `publish_diff` 8/8, `s3_resource_boundary` PASS, `runtime_resource` PASS, `build_cache` 43 assertions across 3 groups, `HotfixRuntimeStateMachine` PASS, and `S2RuntimeBoundary` PASS. The build-cache count is intentionally reduced because redundant direct resolver/source-shape tests were removed after their owning Task implementations became the source of truth. Solution compilation passes with 0 errors and only the existing assembly-conflict warnings. `HotfixContentState`, its `CurrentContent`/`ContentState` chain, `ClientUpdateRequiredInfo`, and `ContentDependencyIndexResolver` are removed; remaining verification checks use the reduced behavior-focused categories. Unity/Player E2E, real AB build matrices, and real publish matrices were not run, per the approved verification boundary.


## Evidence Read During Discussion

- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/ABPipelineBackbone.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/CollectABAssetsTask.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/CollectionScanner.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/AnalyzeABDependenciesTask.cs`
- `Assets/FYAsset/Scripts/AB/Build/Collector/Editor/DependencyAnalysis/DependencyAnalyzer.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/BuildABContentTask.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/GenerateABManifestTask.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/VerifyABContentTask.cs`
- `Assets/FYAsset/Scripts/AB/Build/Pipeline/Editor/Tasks/ExportABOutputTask.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Pipeline/Editor/BuildPipelineRunner.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Editor/BuildProjectRunner.cs`
- `Assets/FYAsset/Scripts/Shared/Build/Editor/Summary/BuildArtifactReuseService.cs`
