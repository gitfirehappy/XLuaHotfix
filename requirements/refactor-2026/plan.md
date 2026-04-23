# Refactor Plan: XLuaHotfix Full Resource Management System Overhaul — Master Plan

> **Status**: In progress (Phase 1-3 completed, Phase S completed, next: Phase 4)
> **Ultimate Goal**: Fully replace Addressables with custom runtime + build-time resource management system (referencing YooAsset architecture)
> **Created**: 2026-03-16
> **Updated**: 2026-04-19 — Phase S complete (S1+S2+S3+S4), serialization infrastructure operational

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
  E4 Dependency analysis -> E5 Build pipeline rewrite -> E6 ABManifest build export -> E7 Diff snapshot adaptation

Phase 7: Raw Files & Special Assets
  F1 RawFile Bundle -> F2 SpriteAtlas linkage -> F3 Platform-specific compression

Phase 8: Editor Tools
  G1 Visual management panel -> G2 Dependency graph -> G3 Build report & estimation

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

### Phase 5: Build-Time - Asset Collection & Indexing

| ID | Content | Reference | Status |
|----|---------|-----------|--------|
| plan-E1-1.md | E1-1: Collector data model — CollectorSetting SO hierarchy (Setting→Package→Group→Collector) + enums (ECollectorType/EPayloadKind/EAssetRole) + AssetClassification struct + rule interfaces (IAddressRule/IPackRule/IFilterRule) + CollectedAssetInfo + RuleResolver. Runtime/Editor assembly split | YooAsset | Approved |
| plan-E1-2.md | E1-2: Classifier (PayloadKind auto-inference + AssetRole mapping) + default rules (AddressByFileName, CollectAll, PackByCollectPath) + EForcePayloadKind enum | YooAsset | Approved |
| plan-E1-3.md | E1-3: Collection scan engine — CollectionScanner static utility (AssetDatabase.FindAssets), Package-scoped deepest-path ownership dedup, IgnorePatterns (simplified gitignore subset: *.ext/dirname//*keyword*), FilterRule→IgnorePatterns execution order, GlobMatcher utility, ScanResult error reporting (7 conditions), Tags merge, PackKey→BundleNameBuilder bundle logical name assembly, GUID uniqueness validation. Depends on E1-1 + E1-2 + E2 (GetPackKey contract + PackRuleContext.Labels + BundleNameBuilder) | YooAsset | Approved |
| plan-E1-4.md | E1-4: Editor UI — BuildPipelineWindow shell (sidebar 5-area routing) + CollectorPanel (IMGUI TreeView 3-level tree, drag reorder, right-click menus) + CollectorPropertyPanel (Package/Group/Collector field editors, rule dropdown via RuleDropdownHelper) + CollectorSettingValidator (9-rule save-time validation). 8 new files, 1 modified | YooAsset | Approved |
| plan-E2.md | E2: PackRule implementations (PackSeparately/PackByDirectory/PackByLabel) + BundleNameBuilder framework utility (3-segment logical name assembly: pkg_group_key) + IPackRule interface change (GetBundleName→GetPackKey, grouping key only) + PackRuleContext Labels field + separator convention (_ between segments, - between labels) + E1-2 PackByCollectPath semantic change (return collectDirName only) + E1-3 scan pipeline sync (labels before PackRule, PackRuleContext struct, BundleNameBuilder.Build). 4 new files, 5 modified (incl. E1-1/E1-2 plan updates + E1-3 scan pipeline sync) | YooAsset | Approved |
| E3 | CANCELLED — All content absorbed by E1-3 (deepest-path dedup, IgnorePatterns, conflict detection). Dev/CI severity policy deferred to E5 build pipeline fail-fast design | YooAsset | CANCELLED |

### Phase 6: Build-Time - Build Pipeline

| ID | Content | Status |
|----|---------|--------|
| E4 | Dependency analysis + static asset GCRoot | To be planned |
| E5 | Build pipeline rewrite (replace Addressables BuildScript) | To be planned |
| E6 | ABManifest build export | To be planned |
| E7 | Diff snapshot adaptation (DifferentialProcessor migration) | To be planned |

### Phase 7: Raw Files & Special Assets

| ID | Content | Status |
|----|---------|--------|
| F1 | RawFile Bundle support | To be planned |
| F2 | SpriteAtlas linked refresh | To be planned |
| F3 | Platform-specific compression strategy | To be planned |

### Phase 8: Editor Tools

| ID | Content | Status |
|----|---------|--------|
| G1 | Visual resource management panel | To be planned |
| G2 | Dependency graph | To be planned |
| G3 | Build report & estimation | To be planned |

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
