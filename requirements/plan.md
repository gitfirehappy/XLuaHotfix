# Refactor Plan: XLuaHotfix Full Resource Management System Overhaul — Master Plan

> **Status**: In progress (Phase 1-4 completed, Phase 5 E1-1/E1-2/E1-3/E1-4/E2 realized, Phase 6 E4/E5-1/E5-2a/E5-2b/E6/E9/E10/E11 realized, review-fix-20260509 and naming-unification executed, E7 pending)
> **Ultimate Goal**: Fully replace Addressables with custom runtime + build-time resource management system (referencing YooAsset architecture)
> **Created**: 2026-03-16
> **Updated**: 2026-05-24 — consolidated recent standalone requirement plans into the shared plan/archive flow

---

## Core Principles (Apply to All Sub-Plans)

1. **No unnecessary changes** — Only refactor explicitly listed parts; leave other files untouched
2. **No added complexity** — New abstraction layers must not introduce more indirection than existing implementation
3. **Preserve existing logic** — Each direction has explicit Invariants that must pass
4. **No paradigm shifts** — XLua bridge system / SO configuration approach preserved; hotfix build pipeline replaced incrementally
5. **Incremental replacement** — Addressable API migrated step by step, no big-bang switch
6. **Explain first** — Code comments must explain rationale when refactoring complex logic
7. **/// comments + #region** — All new files include XML doc comments and region separators, consistent with existing code

---

## Execution Protocol (Mandatory)

```
1. Developer approves sub-plan (confirms approval checklist)
   |
2. Execute sub-plan (implement tasks step by step)
   |
3. Execution complete -> explain changes -> request developer sign-off
   |
4. Developer may ask questions at any time; executor must explain
   |
5. After sign-off -> ask whether to start next sub-plan
   |
6. Not satisfied -> refine current sub-plan (back to step 2)
```

**No code changes without explicit developer approval.**

---

## Full Roadmap

### Phase Overview

```
Phase 1: Runtime Abstraction Layer (completed)
  B1 IAssetIndex -> B2 IPackageBackend -> B3 DialogueDataManager

Phase 2: Runtime Contract Layer (B5-1/B5-2 done, B5-3 cancelled, B5-4 deferred)
  B5-1 Entry Model -> B5-2 Resolve/Load/Handle -> B5-3 CANCELLED -> B5-4 Deferred

Phase 3: Runtime Implementation Layer <- Phase 3 COMPLETE
  B6 ABAssetIndex impl (DONE) -> B7 ABPackageBackend impl (DONE) -> B8 AssetHandle + ref-count pool (DONE)

Phase S: Serialization Infrastructure (cross-cutting, before Phase 4) <- Phase S COMPLETE
  S1 Interface + JsonCodec (DONE) -> S2 BinaryCodec + code generator (DONE) -> S3 ABManifest binary (DONE) -> S4 Runtime integration (DONE)

Phase 4: Hotfix Core Pipeline (B4+B9 merged)
  IHotfixPipeline interface + ABHotfixBackend + LegacyHotfixBackend + orchestrator refactor

Phase 5: Build-Time - Asset Collection & Indexing (ref. YooAsset)
  E1 Collector framework -> E2 Packing rules -> E3 CANCELLED (absorbed by E1-3)

Phase 6: Build-Time - Build Pipeline
  E4 Dependency analysis -> E5 Build pipeline engine -> E6 ABManifest build export -> E7 Diff snapshot adaptation

Phase 7: Delivery & Download Strategy
  F1 Offline built-in package -> F2 Background download -> F3 A/B test variant download

Phase 8: Editor Tools (incremental — inserted after each phase, no standalone G-series)

Phase 9: Advanced Runtime
  H1 AsyncOp priority scheduler (TBD) -> H2 LRU/LFU cache strategy (deferred)

Phase 10: Assembly Splitting (last)
  D0~D4 Modular splitting + glue layer
```

### Key Dependencies

```
Phase 1 --> Phase 2 --> Phase 3 --> Phase S --> Phase 4
  (abstraction) (contract)  (impl)  (serialization) (hotfix core)
                 |                      |               |
                 | entry model format   | unified I/O   | ABManifest format
                 v                      v               v
              Phase 5 --> Phase 6 --------------------------> Phase 7
              (build collect) (build pipeline, uses S2/S3)   (special assets)
                              |
                              v
                          Phase 8 (editor tools)
                              |
                              v
                          Phase 9 (advanced runtime)
                              |
                              v
                          Phase 10 (assembly splitting)
```

**Note**: Phase 3 and Phase 5 can partially run in parallel (sharing entry model format defined in Phase 2).
Phase 4 and Phase 6 must be coordinated (ABManifest runtime consumption + build-time output must align).

---

## Sub-Plan File Index by Phase

### Phase 1: Runtime Abstraction Layer (completed)

| File | Content | Status |
|------|---------|--------|
| plan-B1.md | B1: IAssetIndex asset index layer | DONE |
| plan-B2.md | B2: IPackageBackend asset loading layer | DONE |
| plan-B3.md | B3: DialogueDataManager dual mode | DONE |

### Phase 2: Runtime Contract Layer (B5-1/B5-2 done, B5-3 cancelled, B5-4 deferred)

| File | Content | Status |
|------|---------|--------|
| plan-B5.md | B5 Overview | Approved |
| plan-B5-1.md | B5-1: Runtime entry model | DONE |
| plan-B5-2.md | B5-2: Resolve/Load API + AssetHandle | DONE |
| plan-B5-3.md | B5-3: Validation/diagnostics tools | CANCELLED (belongs to Phase 6 build pipeline) |
| plan-B5-4.md | B5-4: Migration path & legacy API deprecation | Deferred (evolves naturally with implementation) |

### Phase 3: Runtime Implementation Layer

| ID | Content | Status |
|----|---------|--------|
| plan-B6.md | B6: ABAssetIndex implementation (custom index replacing AddressableLabelsConfig runtime role) | DONE |
| plan-B6-manifest.md | ABManifest data layer specification | DONE |
| plan-B7.md | B7: ABPackageBackend overview (custom AB runtime loading backend replacing AddressablesBackend) | DONE |
| plan-B7-1.md | B7-1: ABBundleLoader — Bundle file I/O + dependency resolution + bundle cache | DONE |
| plan-B7-2.md | B7-2: ABPackageBackend — IPackageBackend impl + asset cache + AssetPackageManager integration | DONE |
| plan-B8.md | B8: AssetHandle<T> struct redesign + HandleRegistry + error propagation unification: (1) AssetHandle<T> changed from class to **struct** (value semantic, 0 GC) with HandleId+Generation + HandleRegistry pattern. (2) AssetLoadError.Code expansion (BundleNotFound, BundleLoadFailed, DependencyFailed, AssetExtractionFailed). (3) ABBundleLoader returns `(AssetBundle, AssetLoadError)` tuple (internal API). (4) ABPackageBackend internal tuple API `LoadAssetTupleAsync/Sync`. (5) AssetPackageManager 4 LoadByXxx methods integrated with HandleRegistry.Alloc. IPackageBackend/AddressablesBackend unchanged | DONE |

### Phase 4: Hotfix Core Pipeline

| File | Content | Status |
|------|---------|--------|
| plan-B4.md | B4: Catalog/Locator replacement (original concept doc, superseded by plan-B4B9.md) | Superseded |
| plan-B4B9.md | B4+B9 merged: IHotfixPipeline interface separation + ABHotfixBackend + LegacyHotfixBackend + orchestrator refactor + NetworkDownloader relocation. Constants.USE_AB_BACKEND global switch | DONE |

### Phase S: Serialization Infrastructure (cross-cutting)

| File | Content | Status |
|------|---------|--------|
| plan-serialization.md | Serialization master plan (overview + 4-phase roadmap) | Draft |
| plan-S1.md | S1: ISerializationCodec + JsonCodec + SerializationUtility + replace 10 call sites | DONE |
| plan-S2.md | S2: BinaryCodec infrastructure: [BinarySerializable]/[BinaryField] attributes + BinaryHeader read/write + Editor code generator | DONE |
| plan-S3S4.md | S3: ABManifest data class annotation + code generation + Magic registration; S4: ManifestLoader .bin/.json auto-detect + build-side dual export | DONE |

### Cross-Cutting Utilities

| File | Content | Status |
|------|---------|--------|
| plan-filehelper.md | FileHelper: cross-platform file I/O utility (8 methods: +Exists). Fixes Android StreamingAssets read bug + adds atomic write + unified delete semantics. 1 new file, 3 modified | DONE |
| folder-cleanup-20260518/plan/plan-folder-cleanup.md | FYAsset Hotfix + Runtime folder boundary cleanup: LegacyRuntime retired, Hotfix/Backends split by AB vs Addressables, Runtime/Facade and Runtime/Contracts introduced, empty Helpers/Helper removed | Executed, awaiting sign-off |
| folder-cleanup-20260518/plan/plan-build-folder-cleanup.md | FYAsset Build folder path cleanup: BuildManage retired into Release/Manifests/Bootstrap/Snapshots/Versioning, with Collector/Pipeline/Build UI unchanged | Executed, awaiting sign-off |
| folder-cleanup-20260518/plan/plan-build-aa-ab-boundary-fix.md | Corrective Build AA/AB boundary cleanup: runtime manifests aligned under Runtime/Manifests, LuaScriptsIndex moved to XLuaFramework, Build Release/Editor split by Shared/Addressables/AB responsibilities, Manifest type renamed to PackageIndex | Executed, awaiting sign-off |
| plan-R1.md | R1: Unified error handling architecture — BuildMessage (Editor) + RuntimeMessage (Runtime) separated types, string Code with const files (BuildErrorCodes/RuntimeErrorCodes), Severity on both sides, factory-only construction, AssetLoadError/ScanMessage renamed, PATH_NOT_FOUND fixed to Warning | DONE |
| plan-R2.md | R2: Runtime Correctness + Error Contract Unification + Dedup — HandleRegistry._entryActiveCounts + ABPackageBackend error contract unified + code dedup | DONE |

### Review-Driven Fixes

| File | Content | Status |
|------|---------|--------|
| plan-review-fix-20260506.md | 2026-05-06 E4 editor code quality review: 7 fixes — DependencyAnalyzer HashSet O(1) + catch log + method split + warning simplify + TreeView dead code + RuleResolver.GetRule<T> + DAGScheduler.BuildAdjacencyGraph. net -30 lines | DONE |
| plan-review-fix-20260509.md | 2026-05-09 Three-dimension GPT review fix: 11 tasks — RuntimeAssetEntry Labels guard + IAssetIndex legacy cut + Manager self-cache + CollectorPathUtility extraction + CollectorRef/AssetClassification value semantics + ABAssetIndex zero-alloc + typo fixes + PascalCase convention | **Executed** |
| plan-naming-unification.md | 2026-05-09 Old-pipeline field naming PascalCase unification: VersionState/BundleInfo/Manifest camelCase→PascalCase (9 fields). Complements review-fix T9/T10/T11 | **Executed** |

### Recent Archived Shared Plans

| File | Content | Status |
|------|---------|--------|
| plan-build-repo-diff-module-20260523.md | Build Repository Plan 1/2: artifact diff module extraction and AA transition boundary | Archived |
| plan-build-repository-core-20260523.md | Build Repository Plan 2/2: filesystem JSON repository, automatic build commits, status, and read-only diff preview | Archived |
| plan-build-repository-release-20260523.md | Build Repository Plan 3: AB Push, IPushTarget, PushHistory, Repository CLI, and ConfirmRelease cleanup | Archived |
| plan-hotfix-diff-task-20260524.md | AA/AB current-vs-HEAD diff unified under DAG stop-after flow; PackageIndex writing moved into AA/AB DAG | Archived |
| plan-comment-debug-coverage-20260524.md | Build/repository/hotfix task comments and direct Debug log coverage improved without behavior changes | Archived |

### Phase 5: Build-Time - Asset Collection & Indexing

| ID | Content | Reference | Status |
|----|---------|-----------|--------|
| plan-E1-1.md | E1-1: Collector data model — CollectorSetting SO hierarchy (Setting→Package→Group→Collector) + enums (ECollectorType/EPayloadKind/EAssetRole) + AssetClassification struct + rule interfaces (IAddressRule/IPackRule/IFilterRule) + CollectedAssetInfo + RuleResolver. Runtime/Editor assembly split | YooAsset | DONE |
| plan-E1-2.md | E1-2: Classifier (PayloadKind auto-inference + AssetRole mapping) + default rules (AddressByFileName, CollectAll, PackByCollectPath) + EForcePayloadKind enum | YooAsset | DONE |
| plan-E1-3.md | E1-3: Collection scan engine — CollectionScanner static utility (AssetDatabase.FindAssets), Package-scoped deepest-path ownership dedup, IgnorePatterns (simplified gitignore subset: *.ext/dirname//*keyword*), FilterRule→IgnorePatterns execution order, GlobMatcher utility, ScanResult error reporting (7 conditions), Tags merge, PackKey→BundleNameBuilder bundle logical name assembly, GUID uniqueness validation. Depends on E1-1 + E1-2 + E2 (GetPackKey contract + PackRuleContext.Labels + BundleNameBuilder) | YooAsset | DONE |
| plan-E1-4.md | E1-4: Editor UI — BuildPipelineWindow shell (sidebar 5-area routing) + CollectorPanel (IMGUI TreeView 3-level tree, drag reorder, right-click menus) + CollectorPropertyPanel (Package/Group/Collector field editors, rule dropdown via RuleDropdownHelper) + CollectorSettingValidator (9-rule save-time validation). 8 new files, 1 modified | YooAsset | DONE |
| plan-E1-4-rework.md | E1-4 rework: repair landed Collector editor UI (layout overlap, bounded inspector rendering, empty-state hierarchy, Scan Preview tab) while keeping IMGUI and existing data/scan contracts | Internal follow-up | DONE |
| plan-E2.md | E2: PackRule implementations (PackSeparately/PackByDirectory/PackByLabel) + BundleNameBuilder framework utility (3-segment logical name assembly: pkg_group_key) + IPackRule interface change (GetBundleName→GetPackKey, grouping key only) + PackRuleContext Labels field + separator convention (_ between segments, - between labels) + E1-2 PackByCollectPath semantic change (return collectDirName only) + E1-3 scan pipeline sync (labels before PackRule, PackRuleContext struct, BundleNameBuilder.Build). 4 new files, 5 modified (incl. E1-1/E1-2 plan updates + E1-3 scan pipeline sync) | YooAsset | DONE |
| E3 | CANCELLED — All content absorbed by E1-3 (deepest-path dedup, IgnorePatterns, conflict detection). Dev/CI severity policy deferred to E5 build pipeline fail-fast design | YooAsset | CANCELLED |

### Phase 6: Build-Time - Build Pipeline

| ID | Content | Status |
|----|---------|--------|
| E4 | Dependency analysis + shared extraction (BFS + SharePolicy) | **Realized** (plan-E4.md) |
| E5-1 | Build pipeline core engine — IBuildTask + BuildContext + BuildTaskResult + BuildPipelineConfig SO + BuildTaskResolver + DAGScheduler (Kahn topology + deterministic batch execution + W-W/R-before-W validation + ValidatePair/ValidateAll + SequentialMode). Note: "batch" means logically-independent tasks grouped by topological level, executed sequentially on main thread (Unity Editor single-threaded). 8 new files, 608 lines. **2026-05-07 review fixes: DAGScheduler Read-before-Write unsound + BuildTaskResolver duplicate TaskName fail-fast** | **Realized** (plan-E5-1.md) |
| E5-2a | Backbone Tasks Phase 1 — TaskPrepareContext / TaskCollectBuiltins / TaskBuildBundles + BundleBuildInfo + BundleCompression. **2026-05-07 review fixes: scene output collapse + folder guard + rawfile multi-file** | **Realized** (plan-E5-2a.md) |
| E5-2b | Backbone Tasks Phase 2 — TaskVerifyBuildResult (6 checks) / TaskOrganizeOutput (copy+serialize+summary+cleanup). Includes HashGenerator unification (CRC32 merge + enum) + BuildVerificationResult type | **Realized** (plan-E5-2b.md) |
| E6 | ABManifest build export — TaskGenerateManifest + CRC32Helper + BundleType int→string | **Realized** (plan-E6.md) |
| E7 | Diff snapshot adaptation → **Build Repository** (统一 git-like 版本管理系统，合并原 E7 + Smart Versioning) | **Draft** (draft-build-repository-20260518.md) — 2026-05-18 重新设计：7 操作（status/add/diff/commit/reset/tag/push）、统一 ArtifactDigest 数据结构、IArtifactScanner 注入 AA/AB 差异、apply 移出 Repository |
| E9 | VersionNumber SemVer+Build extension (Major.Minor.Patch + Build + Channel, IComparable, Parse/TryParse, operator overloads). Prerequisite for E7 | **Realized** (plan-E9-version.md) |
| E10 | BuildProjectManager dual-backend split — `IBuildBackend` + `LegacyAddressableBuildBackend` + `ABBuildBackend` + orchestrator-style `BuildProjectManager`. `BuildCommandLine` kept on the same public API path. AB output layout aligned to `{PackageRoot}/bundles/` to match hotfix/runtime contracts | **Realized** (plan-E10-buildbackend.md) |
| E11 | FYAssetSettings SO — new `FYAssetSettings` ScriptableObject (Runtime assembly) replaces `FYAssetConstants`; all configurable fields (ProjectName, HotfixUrl, UseABBackend, paths) become SO instance fields; `static const` members preserved on SO type; `BuildPipelineConfig.DefaultBackendMode` removed; `SettingsPanel` added; `BuildPipelineWindow` sidebar reorganized to SETTINGS → AB PIPELINE → MANAGE; AB PIPELINE grayed-out when `UseABBackend=false`; `FYAssetConstants.cs` deleted | **Realized** (plan-E11-settings.md) |
| E12 | PipelinePanel BuildGraph and build execution editor — staged GraphView upgrade for AB pipeline. E12-1 reworked 2026-05-14: read-only DAG visualization moved from Builder to Pipeline, Build options (`FileNameStyle`, `BundleCompression`, `SequentialMode`) moved to Pipeline top bar, right-click optional-task creation excludes backbone tasks, Reload and `DAGScheduler.Validate()` summary remain read-only. E12-2 executed 2026-05-14: Pipeline Build Mode + Build button trigger existing Full/Hotfix flows through `BuildProjectManager`, `DAGScheduler` emits observer/callback-driven task statuses, and unused `BuildGraphToolbar` was removed. Builder/report querying is deferred to a separate post-E7 plan because E7 owns diff snapshot and digest outputs. | **E12-2 Executed; awaiting sign-off** (plan-E12-buildgraph-editor.md) |
| E13 | Legacy Pipeline 侧边栏重组 + 面板骨架 — BuildPipelineWindow 侧边栏 4 组（SETTINGS/LEGACY PIPELINE/AB PIPELINE/MANAGE）+ 互斥灰显 + 折叠侧栏组 + LegacyConfigPanel（Addressables summary + open groups window）+ LegacyBuildPanel/LegacyReportPanel 占位。可并行 E7 | **Executed** (plan-E13-legacy-sidebar.md) — T1-T4 代码已落地 2026-05-15，dotnet build 0 errors |

### Phase 7: Delivery & Download Strategy

| ID | Content | Status |
|----|---------|--------|
| F1 | Offline built-in package (DeliveryMode: Streamed/Builtin/Hybrid) | Ideas (plan-F-ideas.md) |
| F2 | Background download (BackgroundDownloadManager + Bundle Tags) | Ideas (plan-F-ideas.md) |
| F3 | A/B test variant download (VariantIndex + ABTestManager) | Ideas (plan-F-ideas.md) |

> **Note**: Original F1 (RawFile Bundle) / F2 (SpriteAtlas) / F3 (Platform compression) absorbed into unified pipeline + 5 extension points (see plan-E-draft.md F-series convergence). RawFile handled via PayloadKind routing + IPackageBackend.LoadRawFile; SpriteAtlas via E4 dependency analysis; compression via IAssetImportRule.

### Phase 8: Editor Tools

> **Strategy**: Editor tools are built incrementally as validation closure for each phase — not a standalone G-series. Insertion points by phase completion:
>
> | After | Editor Delivery |
> |-------|----------------|
> | Phase 5 (E1-1~E1-4 + E2 complete) | Collector panel (E1-4, Approved) + scan result preview |
> | Phase 6 E5-1/E5-2 | PipelinePanel BuildGraph editor (E12: E12-1 read-only visualization reworked into Pipeline; optional Task creation via graph right-click; E12-2 build trigger/status executed) |
> | Phase 6 E4+E6 | Inspector panel (Bundle table + asset search) |
> | Phase 6 closed | Settings panel finalization |
>
> Each insertion point produces its own precise sub-plan at that time. No empty G1/G2/G3 plan files.

### Phase 9: Advanced Runtime

| ID | Content | Status |
|----|---------|--------|
| H1 | AsyncOperation priority scheduler + CancellationToken support (load cancellation for scene switch/timeout/lifecycle) + IProgress<float> progress callbacks. Note: Unity AssetBundle.LoadFromFileAsync doesn't support native cancellation — "cancel" means "stop caring about result", bundle still loads then discards. CancellationToken + refcount rollback interaction is the main complexity source | TBD |
| H2 | LRU/LFU cache strategy | Deferred |

### Phase 10: Assembly Splitting

| File | Content | Status |
|------|---------|--------|
| plan-D.md | D0~D4: Modular splitting + glue layer | Pending approval (execute last) |

---

## Completed Items (Non-Resource-Management)

| File | Content | Status |
|------|---------|--------|
| plan-C.md | Lua script directory auto-management | DONE (C1+C2), C3 after Plan-B |
| plan-A.md | UI framework optimization | DONE |

---

## Change Log

| Date | Change |
|------|--------|
| 2026-03-16 | Initial version: three-direction refactoring |
| 2026-03-16 | Added UIAnimation configurable fade-in/fade-out duration |
| 2026-03-16 | plan-B expanded: group labels + catalog mechanism, split into B1-B4 stages |
| 2026-03-16 | DialogueDataManager kept as independent dual-mode (Standalone default) |
| 2026-03-16 | plan-A added multi-Canvas coordination notes + DynamicGroup responsibility extension |
| 2026-03-16 | New rule: must explain rationale when refactoring complex logic; developer can ask questions |
| 2026-03-17 | Approval complete: Plan-C/A/B1/B2 all passed. A3 ViewModel deferred; DynamicGroup not extended, only clarified responsibilities |
| 2026-03-17 | Plan-B2 addendum: must support async loading (LoadFromFileAsync); path strategy is hotfix dir first + fallback StreamingAssets |
| 2026-03-17 | Plan-C addendum: adopted Option 2 (SO separation + config mapping), LuaAutoSyncConfig added outputDirectory field |
| 2026-03-29 | New Plan-B5: stabilize runtime entry model, Resolve/Load contract, Handle, validation & migration strategy before B4 |
| 2026-03-30 | **Roadmap expansion**: Upgraded from three-system refactoring to full custom resource management system. Added Phase 3-10 covering runtime impl, build-time overhaul (ref. YooAsset), RawFile, editor tools, advanced runtime, assembly splitting. Plan-D moved to last. LRU/LFU deferred, AsyncOp scheduler TBD |
| 2026-04-01 | YooAsset knowledge base (5 module files) written to context/dependencies/. B6 design review completed (7 review points). B6 coded: ABAssetIndex 237 lines + ManifestLoader 84 lines + AssetPackageManager integration |
| 2026-04-07 | B7 plan drafted: split into B7-1 (ABBundleLoader: bundle I/O + deps + cache) + B7-2 (ABPackageBackend: IPackageBackend impl + asset cache + integration). Old-vs-new architecture comparison completed. 8 design decisions documented. Awaiting approval |
| 2026-04-07 | ManifestBundleEntry field extension decisions: BundleType (reserved serialized field, default 0, assigned by Phase 6 build pipeline) + ReferencedByBundleIndices (runtime-only, built in Initialize() step 7). Tags semantics clarified as bundle-level download strategy tags. IsImplicitDependency deferred to Phase 5 E1 Collector framework. E1 description updated to include IsImplicitDependency |
| 2026-04-07 | FormatVersion field removed from ABManifest — no consumer in single-project context (Manifest format tied to APP version). Constants.MANIFEST_FORMAT_VERSION also removed |
| 2026-04-07 | Error handling & load state decisions: (1) B8 scope expanded to include error propagation unification (AssetLoadError.Code expansion + ABBundleLoader structured errors + ABPackageBackend returns AssetHandle<T> for sync/async). (2) CancellationToken/cancellation deferred to H1 (AsyncOp scheduler, Phase 9) — Unity ABLoadFromFileAsync not natively cancellable + refcount rollback complexity. (3) Retry strategy placed in B9 at HotfixManager/download layer. (4) Load progress callbacks in H1 |
| 2026-04-07 | B8 AssetHandle struct redesign confirmed: AssetHandle<T> from class to struct (value semantic, 0 GC, ref. Addressables pattern). struct Handle (version + operationId) + HandleRegistry. No Pool for struct itself. Internal API convention: ValueTuple. External API convention: AssetHandle<T> struct. Research prerequisite: Addressables AsyncOperationHandle.cs (local) + YooAsset OperationHandleBase (GitHub) |
| 2026-04-08 | Plan synchronization update: aligned plan-B / plan-B5* / plan-B7* execution status with progress log and added plan-B8.md to sub-plan index |
| 2026-04-18 | **Serialization infrastructure added**: New Phase S (cross-cutting, before Phase 4). Technical route: zero-dependency custom binary + editor code generator. S1 (interface + JsonCodec) plan written. Key decisions: lightweight binary header (Magic 4B + SchemaVersion 2B + Flags 2B), auto format detection (Magic → binary, else → JSON fallback), per-type independent Magic values, old backend artifacts (version_state/BuildIndex) not binary-ized — natural retirement |
| 2026-04-18 | **Phase 4 B4+B9 merged**: IHotfixPipeline interface separation + AB/Legacy dual backend. Key decisions: (1) Interface+backend pattern matching AssetPackageManager. (2) 5-method fine-grained interface (InitBackend/LoadLocalVersion/FetchRemoteVersion/GetBundleDownloadList/PostDownload). (3) HotfixManager stays static, refactored to orchestrator. (4) Constants.USE_AB_BACKEND global switch replaces per-class USE_AB_INDEX. (5) VersionState retires with Legacy backend. (6) NetworkDownloader relocated to Helpers/. (7) AB backend downloads ABManifest.bin/json instead of version_state+catalog (1 fewer network request) |
| 2026-04-18 | **E1-3 plan written**: CollectionScanner static utility + Package-scoped deepest-path ownership + IgnorePatterns simplified gitignore subset (*.ext/dirname//*keyword*) + GlobMatcher + ScanResult error reporting (7 conditions). Key decisions: (1) AssetDatabase.FindAssets for discovery. (2) Cross-Package overlap = error, Package-internal deepest-path dedup. (3) IgnorePatterns as List\<string\> on Collector (not interface). (4) Execution order: FindAssets→exclude sub-paths→FilterRule→IgnorePatterns→Classify/Address/Pack/Tags. (5) Full scan each time, no incremental cache
| 2026-04-19 | **Phase S complete**: Serialization infrastructure operational. S1 (ISerializationCodec + JsonCodec + SerializationUtility) → S2 (BinaryCodec + code generator + attributes) → S3 (ABManifest binary annotation + 4 serializers generated + Magic registration) → S4 (ManifestLoader .bin/.json auto-detect + LocalStatusExporter dual export + ABManifest.DeserializeFromFile). Key deliverables: zero-dependency binary serialization, auto format detection, round-trip verified |
| 2026-04-21 | **review-fix-01 completed**: Repaired 4 runtime review findings. ABBundleLoader now reads bundles from `CurrentGUIDRoot/bundles` + `StreamingAssets/bundles` and fails fast on dependency cycles. ABPackageBackend now uses EntryId as cache/release identity (Address remains query input only). Legacy `AssetHandle.Release()` restored pre-interface behavior by releasing via resolved address. |
| 2026-04-23 | **E1-4 plan written**: BuildPipelineWindow shell (sidebar 5-area routing, only Collector implemented) + CollectorPanel (IMGUI TreeView 3-level tree, same-level drag reorder, right-click Add/Delete/Duplicate) + CollectorPropertyPanel (Package/Group/Collector field editors, RuleDropdownHelper reflection-based rule dropdown) + CollectorSettingValidator (9-rule save-time validation with bottom-area display). Key decisions: (1) Full shell Option A — future panels fill into existing framework. (2) IMGUI TreeView — consistent with project style. (3) Same-level drag only, cross-level via copy+delete. (4) Rule dropdown auto-scans implementations. (5) Save-time validation via ApplyModifiedProperties. 8 new files, 1 modified |
| 2026-04-23 | **E2 plan written + rev2 sync fix**: PackRule implementations (PackSeparately/PackByDirectory/PackByLabel) + BundleNameBuilder framework utility. Key decisions: (1) IPackRule interface change GetBundleName→GetPackKey — PackRule outputs grouping key only, framework assembles name. (2) BundleNameBuilder 3-segment format: pkg_group_key, all lowercase, SanitizeSegment. (3) Separator convention: `_` between segments, `-` between labels. (4) PackRuleContext gains Labels field. (5) PackByLabel: sorted lowercase labels joined by hyphen, empty→`unlabeled`. (6) PackByDirectory: sub-dir name, root fallback to CollectPath last segment. (7) RawFile unified naming. (8) Hash/extension deferred to E5. (9) Risk upgraded to Low-Medium with compatibility boundary (new pipeline only, no Addressables impact). Cross-plan sync: E1-2 PackByCollectPath semantic change (return collectDirName only, not full name); E1-3 scan pipeline steps reordered (labels before PackRule) + call signature aligned to PackRuleContext struct + BundleNameBuilder.Build. 4 new files, 5 modified (including E1-1/E1-2 plan updates + E1-3 scan pipeline sync) |
| 2026-04-23 | **E3 CANCELLED**: Gap analysis confirmed 11/12 E3 items fully absorbed by E1-3 (deepest-path dedup, IgnorePatterns, CROSS_PACKAGE_OVERLAP/SAME_PATH_CONFLICT, excludedPaths, unique attribution). Sole uncovered item (Dev/CI conflict severity policy) deferred to E5 build pipeline fail-fast design — severity differentiation is a build-task caller decision, not Scanner internal logic |
| 2026-04-25 | **E1-1 completed**: Implemented Collector foundation under `Assets/FYAsset/Scripts/Build/Collector/` — runtime data model (`CollectorSetting`, `CollectorPackage`, `CollectorGroup`, `Collector`, enums, `AssetClassification`) + editor rule contracts (`IAddressRule`, `IPackRule.GetPackKey`, `IFilterRule`, `CollectedAssetInfo`, `RuleResolver`). `Constants.cs` gained collector asset path + built-in rule name constants. Verification also synced `Assembly-CSharp.csproj` / `Assembly-CSharp-Editor.csproj` so `dotnet build XLuaHotfix.sln` compiles the new files. Build passed with 0 errors, existing warnings only |
| 2026-04-25 | **E1-2 completed**: Implemented `AssetClassifier` + three default rules (`AddressByFileName`, `CollectAll`, `PackByCollectPath`) under `Assets/FYAsset/Scripts/Build/Collector/Editor/`. `AssetAddressGenerator` added a shared `GenerateShortAddress(assetPath, primaryType, useTypeSuffix)` entry so collector address rules reuse the existing B5 naming contract instead of forking logic. External verification updated `Assembly-CSharp-Editor.csproj` to include the new editor files, and `dotnet build XLuaHotfix.sln` passed with 0 errors, existing warnings only |
| 2026-04-28 | **Plan gap convergence**: (1) E7 precise sub-plan written (plan-E7.md): IDiffPipeline 5-method interface + LegacyDiffBackend/ABDiffBackend separation + BundleDigestList .bin/.json persistence + head.json per-version snapshot history + each backend produces own delta type. 10 design decisions, 12 tasks, 8 new files. (2) F-series renumbered: F1/F2/F3 now cover offline package / background download / A/B test. Original RawFile/SpriteAtlas/Compression absorbed into unified pipeline extension points. (3) G-series replaced with incremental insertion point strategy — editor tools built as validation closure after each phase, not a standalone series. (4) YooAsset gap analysis resolved: all 11 decision items closed. #1 Shader→TaskCollectBuiltins, #2 Verify→TaskVerifyBuildResult, #3 Tags→E6 union aggregation, #4 Cleanup→PackageCleaner already covered, #5 Report→TaskOrganizeOutput already covered, #6 Naming→E5-1 BundleFileNameStyle enum, #7 Toggle→E1-4 CollectorGroup.Enabled, #8 Retry→deferred to mobile testing |
| 2026-05-07 | **E5-2b realized + E5 pipeline fully landed**: TaskVerifyBuildResult (6 checks) + TaskOrganizeOutput + HashGenerator unification (CRC32 merge + HashAlgorithmType enum) + BuildVerificationResult type. E6 review fixes applied (BuildVersion removed from ReadKeys, CRC32 file-missing→fail-fast, Tags comment updated) |
| 2026-05-08 | **E7+E9 audit fixes**: External review identified 8 findings (6 valid, 1 partial, 1 non-issue). E7: added T9 (TaskBuildBundles reads BundleDelta for incremental rebuild), T11 (DAGScheduler→BuildCommandLine integration), fixed DiffResult description, added ConfirmRelease history-overwrite guard. E9: corrected version format to SemVer 2.0 (`X.Y.Z-channel+build`), clarified binary compat (no fallback, delete old .bin), added T6 (TaskPrepareContext writes VersionNumber), added Expected Consumers table |
| 2026-05-09 | **Three-dimension GPT review fix plan approved**: 11 tasks covering data-structure hardening (RuntimeAssetEntry Labels guard + CollectorRef/AssetClassification value semantics + ABAssetIndex zero-alloc), architecture redundancy removal (IAssetIndex legacy cut + Manager self-cache + CollectorPathUtility extraction), naming stabilization (typo fixes + PascalCase convention). 22 files affected. Awaiting execution approval |
| 2026-05-09 | **E9 VersionNumber approved**: 6 tasks, ~80 lines net. SemVer 2.0 format (Major.Minor.Patch-channel+build), IComparable, Parse/TryParse, operators |
| 2026-05-09 | **naming-unification plan promoted**: drafts→plan, 5 tasks, 6 files. VersionState/BundleInfo/Manifest camelCase→PascalCase |
| 2026-05-11 | **E10 executed**: `BuildProjectManager` split into orchestrator + `IBuildBackend` implementations (`LegacyAddressableBuildBackend` / `ABBuildBackend`), `BuildCommandLine` kept on unchanged public API path, AB package layout aligned to runtime `bundles/` contract. Sandbox blocked external `dotnet build` confirmation because access to `C:\Users\cfy\AppData\Local\Microsoft SDKs` was denied |
| 2026-05-13 | **E12 BuildGraph editor approved**: promoted `draft-buildgraph-visualization.md` to `plan-E12-buildgraph-editor.md`; first executable slice is read-only BuilderPanel DAG visualization + Validate only. Editing and build-trigger phases require separate approval |
| 2026-05-14 | **E12-1 executed**: `BuildGraphView` + `BuildTaskNode` + `BuildGraphLayoutEngine` + `BuildGraphToolbar` + `EdgeStyle` created; initial implementation placed DAG in BuilderPanel; `Assembly-CSharp-Editor.csproj` synced; `dotnet build` passed 0 errors; `context/`, `docs` HTML, `progress.txt` aligned |
| 2026-05-14 | **E12-1 reworked**: DAG moved from BuilderPanel to PipelinePanel; Pipeline top bar now owns `FileNameStyle`, `BundleCompression`, and `SequentialMode`; Task list is no longer exposed as a normal inspector list and optional tasks are created from the graph right-click menu with backbone tasks excluded; BuilderPanel no longer hosts the DAG; `dotnet build` passed 0 errors |
| 2026-05-14 | **E12-2 executed**: Pipeline top bar gained `Build Mode` + `Build`; build trigger validates first and routes through `BuildProjectManager`; `BuildExecutionOptions` / `BuildTaskExecutionEvent` / `BuildTaskExecutionStatus` carry DAGScheduler status events into BuildGraph nodes; unused `BuildGraphToolbar` removed; Builder report/query remains deferred until after E7 |
| 2026-05-24 | **Requirements cleanup**: recent standalone requirement plans were archived into `requirements/plan/archive/`, standalone progress was summarized into `requirements/progress.txt` while preserving original requirement-local logs, and new independent per-requirement plan files/folders are disallowed unless explicitly requested by the developer |
