# Refactor Plan: XLuaHotfix Full Resource Management System Overhaul — Master Plan

> **Status**: In progress (Phase 1 completed, Phase 2 B5-1/B5-2 done, Phase 3 B6/B7/B8 done)
> **Ultimate Goal**: Fully replace Addressables with custom runtime + build-time resource management system (referencing YooAsset architecture)
> **Created**: 2026-03-16
> **Updated**: 2026-04-07 — Phase 3 complete (B6+B7+B8), next: Phase 4

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

Phase 4: Hotfix Core Pipeline <- current focus
  B4 Catalog/Locator replacement -> B9 ABManifest format + incremental download adaptation

Phase 5: Build-Time - Asset Collection & Indexing (ref. YooAsset)
  E1 Collector framework -> E2 Packing rules -> E3 Sub-directory collector + ignore rules

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
Phase 1 --> Phase 2 --> Phase 3 --> Phase 4
  (abstraction) (contract)  (impl)    (hotfix core)
                 |                      |
                 | entry model format   | ABManifest format
                 v                      v
              Phase 5 --> Phase 6 --> Phase 7
              (build collect) (build pipeline) (special assets)
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
| B8 | AssetHandle<T> struct redesign + HandleRegistry + error propagation unification: (1) AssetHandle<T> changed from class to **struct** (value semantic, 0 GC) with HandleId+Generation + HandleRegistry pattern. (2) AssetLoadError.Code expansion (BundleNotFound, BundleLoadFailed, DependencyFailed, AssetExtractionFailed). (3) ABBundleLoader returns `(AssetBundle, AssetLoadError)` tuple (internal API). (4) ABPackageBackend internal tuple API `LoadAssetTupleAsync/Sync`. (5) AssetPackageManager 4 LoadByXxx methods integrated with HandleRegistry.Alloc. IPackageBackend/AddressablesBackend unchanged | DONE |

### Phase 4: Hotfix Core Pipeline

| File | Content | Status |
|------|---------|--------|
| plan-B4.md | B4: Catalog/Locator replacement | Concept stage |
| B9 | ABManifest format + incremental download adaptation + download retry strategy (retry at HotfixManager/download layer, not at BundleLoader layer) | To be planned |

### Phase 5: Build-Time - Asset Collection & Indexing

| ID | Content | Reference | Status |
|----|---------|-----------|--------|
| E1 | Collector framework (Collector: Main/Static/Depend + Classifier) + **IsImplicitDependency** field on ManifestAssetEntry (distinguish entry assets from implicit dependencies pulled in by reference) | YooAsset | To be planned |
| E2 | Packing rules (Collect/GroupBy/Pack three-rule separation) | YooAsset | To be planned |
| E3 | Sub-directory collector + ignore rules (gitignore style) | YooAsset + initial ideas | To be planned |

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
