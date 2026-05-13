# FYAsset Redundancy And Simplification Review

Date: 2026-05-08
Scope: `Assets/FYAsset`
Focus: code redundancy, architecture redundancy, and opportunities to make the landed FYAsset implementation leaner and more elegant without changing the already-established behavior surface.
Author: `gpt-5.4/codex`
**Processed**: 2026-05-11 · Duplication partially addressed in E10 (BuildBackend split) and review-fix-20260509 (Collector path tools dedup). Full redundancy elimination remains ongoing (infra-consistency plan P2 targets build artifact duplication).
**Status**: 📦 Archived · Streamlined 2026-05-11

## Summary

FYAsset shows a "feature landed in stages" signature. Key duplication areas: hotfix backend data loading/conversion, manifest/index query surfaces, collector path/rule utilities, AB/Legacy branching inside `AssetPackageManager`.

## Findings

### [P1] Collector editor infrastructure duplicates the same path/overlap/ignore-pattern logic across multiple classes

Files:
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:60`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:305`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:663`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:170`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:217`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:296`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorSettingValidator.cs:129`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorSettingValidator.cs:243`

What is redundant:
- `NormalizePath` exists in at least `CollectionScanner`, `CollectorReverseIndex`, `CollectorSettingValidator`, and several UI helpers.
- cross-package path overlap / containment checks are implemented independently in both `CollectionScanner` and `CollectorSettingValidator`.
- ignore-pattern matching logic is duplicated between `CollectionScanner` and `CollectorReverseIndex`.
- file/folder collect-path existence checks also reappear in multiple places.

Why this matters: These encode real collector semantics. Once copies drift, editor validation, scan result, and reverse-index behavior will stop matching.

Recommendation:
- extract a single editor-only `CollectorPathUtility` / `CollectorPathRules` helper set.
- move into it:
  - path normalization,
  - path containment/depth,
  - file/folder collect-path validation,
  - ignore-pattern matching,
  - cross-package overlap checks.

This is the strongest simplification opportunity in the landed FYAsset code.

### [P1] Hotfix backends are correctly separated by interface, but still duplicate most of the backend template flow

Files:
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:67`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:92`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:118`
- `Assets/FYAsset/Scripts/LegacyRuntime/LegacyHotfixBackend.cs:171`
- `Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:49`
- `Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:81`
- `Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:115`
- `Assets/FYAsset/Scripts/LegacyRuntime/ABHotfixBackend.cs:160`

What is redundant:
- both backends implement the same four-step pattern:
  - load local metadata,
  - fetch remote metadata,
  - convert backend-specific model into `HotfixVersionInfo`,
  - expose `remoteInfo?.Bundles ?? Array.Empty<BundleDownloadItem>()`.
- each backend also keeps its own "remote raw data cache + parsed metadata object + converter" pattern.

Why this matters:
- the interface split is good and should stay;
- the duplication is inside the implementations, where the lifecycle template is almost identical and only the data source differs.

Recommendation:
- keep `IHotfixPipeline`, but extract a shared generic/template layer or helper set for:
  - "load local or null",
  - "fetch remote raw payload",
  - "parse payload into backend model",
  - "convert backend model to `HotfixVersionInfo`".
- at minimum, centralize the `HotfixVersionInfo` assembly pattern to avoid two nearly identical conversion methods.

This is not about merging the two backends into one class. It is about removing duplicated scaffolding while preserving behavioral separation.

### [P1] `ABManifest` and `ABAssetIndex` currently build and maintain overlapping indexes over the same data

Files:
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:44`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:77`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:22`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:66`

What is redundant:
- `ABManifest.Initialize()` builds:
  - `_entryIdIndex`
  - `_addressIndex`
  - `_typeIndex`
  - `_labelIndex`
  - `_bundleNameIndex`
- `ABAssetIndex.BuildIndex()` rebuilds:
  - `_entryIdIndex`
  - `_addressIndex`
  - `_typeIndex`
  - `_labelIndex`
- and first converts every `ManifestAssetEntry` into a new `RuntimeAssetEntry`.

Why this matters: Duplicate indexing architecture over the same manifest payload. One layer is manifest-oriented, the other runtime-entry-oriented, but query dimensions mostly overlap. Result: extra init cost, duplicated query logic, two places to evolve when index semantics change.

Recommendation:
- decide which layer is the true owner of runtime query indexing.
- options:
  - make `ABManifest` a serialization/data container and move query ownership to `ABAssetIndex`;
  - or let `ABAssetIndex` become a thin adapter over prebuilt manifest indexes rather than rebuilding them.

The current split is understandable during migration, but it is heavier than necessary for a settled architecture.

### [P1] `IAssetIndex` currently mixes two eras of abstraction, which keeps legacy compatibility but also bakes redundancy into every caller

Files:
- `Assets/FYAsset/Scripts/Interfaces/IAssetIndex.cs:13`
- `Assets/FYAsset/Scripts/Interfaces/IAssetIndex.cs:28`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:11`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:128`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:116`

What is redundant:
- `IAssetIndex` still carries legacy string-key methods:
  - `GetKeysByLabel`
  - `GetKeysByType`
  - `GetLabels`
  - `ContainsKey`
- while also carrying the newer entry-centric methods:
  - `GetEntryById`
  - `GetEntriesByAddress`
  - `GetEntriesByAddressAndType`
  - `GetAllEntries`
- old implementation (`AddressableLabelsConfig`) supports only the first set;
- new implementation (`ABAssetIndex`) supports both by rebuilding two conceptual views.

Why this matters: A migration bridge that has become a structural burden. Callers such as `AssetPackageManager` maintain key-based APIs and extra label caches because the interface privileges the old query model.

Recommendation:
- define explicit layers instead of one hybrid interface:
  - a legacy string-key query surface for backward compatibility;
  - a canonical entry-query surface for new code.
- then adapt legacy callers at the boundary instead of making every implementation speak both dialects forever.

### [P1] `AssetPackageManager` contains repeated AB/Legacy branching that should be policy-based instead of hand-expanded

Files:
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:26`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:56`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:85`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:360`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:378`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:400`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:426`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:441`

What is redundant:
- initialization is split into `InitializeWithABIndex()` and `InitializeWithLegacyIndex()`;
- resolved loading is split into:
  - `LoadResolvedWithABAsync`
  - `LoadResolvedWithABSync`
  - `LoadResolvedWithLegacyAsync`
  - `LoadResolvedWithLegacySync`
- handle creation is split into:
  - `CreateABHandle`
  - `CreateLegacyHandle`

Why this matters: The subsystem has `IPackageBackend`, but `AssetPackageManager` still knows too much about concrete backend flavor, becoming a second dispatch layer instead of a thin orchestrator.

Recommendation:
- push backend-specific handle-release policy behind an interface or adapter so `AssetPackageManager` does not need four resolved-load variants.
- the manager should ideally do:
  - resolve entry,
  - ask backend for a load result + release policy,
  - wrap handle once.

Right now the architecture is abstracted at the type level but still duplicated at the control-flow level.

### [P2] `ABPackageBackend` has avoidable API duplication between public load methods and internal tuple-based load methods

Files:
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:97`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:117`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:140`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:159`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:271`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:294`

What is redundant:
- there are two public async/sync entry points returning `(asset, error)`;
- and two internal async/sync entry points returning `(asset, bundleName, error)`;
- each pair repeats asset-entry resolution and cache lookup.

Why this matters:
- this duplication is local and manageable today, but it is exactly the sort of "one more overload" growth that eventually makes backend classes noisy.

Recommendation:
- collapse onto one internal canonical load path that returns a richer internal result type;
- let the simpler public API project that richer result instead of redoing resolution/caching preamble.

### [P2] `AddressablesBackend` and `ABPackageBackend` are both valid backends, but the current interface shape still forces asymmetric duplication and special cases

Files:
- `Assets/FYAsset/Scripts/Runtime/Backends/Addressables/AddressablesBackend.cs:24`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:97`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:117`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:362`

What is redundant:
- `ABPackageBackend` needed to grow extra overloads and extra unload entry points (`UnloadByEntryId`) that `AddressablesBackend` conceptually does not share.
- `AssetPackageManager` then compensates with type checks and separate AB/Legacy handling.

Why this matters: The current `IPackageBackend` abstraction does not carry enough semantics for the evolved AB path, creating redundancy through side channels.

Recommendation:
- either enrich the backend contract with an explicit "resolved entry load" concept;
- or introduce a second backend capability interface for entry-aware backends.

That would simplify the manager and remove the need for backend-specific shadow APIs.

### [P2] `AddressableLabelsConfig` and `ABAssetIndex` both solve indexing, but in two different structural styles that now coexist awkwardly

Files:
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:13`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:63`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:54`

What is redundant:
- `AddressableLabelsConfig` serializes precomputed `Type -> Keys` and `Label -> Keys` lists, then rebuilds runtime dictionaries.
- `ABAssetIndex` constructs fresh dictionaries directly from manifest asset entries.
- both exist to answer similar runtime discovery queries, but their internal representations and capabilities differ significantly.

Why this matters:
- maintaining two unrelated indexing models increases conceptual load for everyone touching runtime lookup.
- it also makes migration harder because optimizations or semantics fixes do not transfer naturally between the two.

Recommendation:
- if Legacy must remain, treat it as a compatibility adapter over a canonical query model instead of a peer indexing architecture.

### [P2] Collector rule resolution is duplicated between validation and scanning, but at different abstraction levels

Files:
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:214`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:411`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorSettingValidator.cs:200`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorSettingValidator.cs:223`

What is redundant:
- `CollectionScanner` resolves concrete rule instances with `ResolveRuleSafe<T>()`;
- `CollectorSettingValidator` separately re-validates resolvability through its nested `Resolver` helper.

Why this matters:
- the scanner and validator are checking the same underlying concept, but through different local utilities.
- if rule-resolution semantics change, one side can lag.

Recommendation:
- centralize "rule can resolve" and "rule resolve or message" helpers into one editor-side rule utility.

### [P3] Numerous small `NormalizePath` clones in UI/editor helpers create low-grade noise and unnecessary maintenance surface

Files:
- `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorAssetInspectorGUI.cs:156`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorContextMenu.cs:161`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorPanel.cs:726`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/CollectorTargetPickerPopup.cs:160`

Why this matters:
- each copy is trivial, but the cumulative effect is noisy and makes the collector editor subsystem feel more fragmented than it is.
- this is exactly the sort of redundancy that weakens elegance even when it is not dangerous.

Recommendation:
- once the collector path utility exists, remove all local `NormalizePath` clones and use the shared helper everywhere.

## What should not be "simplified away"

Not all duplication here is bad duplication. Some redundancy is currently buying safety during migration:

- keeping both AB and Legacy runtime paths is reasonable while the Addressables path is still live;
- keeping both manifest-oriented and runtime-entry-oriented views is understandable while the newer resolve/load model is settling;
- maintaining separate build-time and runtime models is fine where serialization boundaries genuinely differ.

The target should be:
- remove mechanical duplication,
- keep intentional boundary duplication.

## Suggested simplification order

1. Extract collector shared utilities.
   This gives the highest code shrink and reduces semantic drift risk immediately.

2. Refactor `AssetPackageManager` around backend capabilities instead of backend type checks.
   This removes repeated AB/Legacy load branches.

3. Consolidate hotfix backend template logic.
   Keep two backends, remove repeated scaffolding.

4. Choose a single canonical owner for AB runtime indexing.
   `ABManifest` and `ABAssetIndex` should not both be full query engines forever.

5. Split legacy query compatibility from canonical entry-query abstraction.
   This lets `IAssetIndex` stop carrying two eras of API long-term.

## Overall assessment

FYAsset's landed implementation is serviceable but carries noticeable migration-era redundancy. The main simplification opportunity: removing repeated semantic logic and dispatch layers so collector behavior is defined once, backend differences stay in backends, manifest/index responsibilities stop overlapping, and compatibility layers stop shaping the whole architecture.

Signature: `gpt-5.4/codex`
