# FYAsset Naming Boundary Maintainability Review

Date: 2026-05-08
Scope: `Assets/FYAsset`
Focus: naming quality, interface boundary clarity, and maintainability consistency across the landed FYAsset implementation.
Author: `gpt-5.4/codex`
**Processed**: 2026-05-11 · Naming normalization partially addressed (naming-unification plan executed). Interface boundary improvements in E10 (IBuildBackend). Remaining style inconsistencies tracked as tech debt.
**Status**: 📦 Archived

## Summary

From this angle, FYAsset's main issue is not raw correctness. It is that the subsystem is carrying multiple generations of style at once:

1. naming is mostly understandable, but not fully normalized;
2. several interfaces exist, yet concrete semantics still leak through them;
3. similar concepts are modeled with different conventions depending on which phase of the refactor they came from.

The result is maintainable code in the short term, but rising cognitive cost in the medium term. New contributors have to infer too much from history:
- which names are legacy,
- which boundaries are real,
- which abstractions are transitional,
- which style should be copied for the next file.

## Findings

### [P1] Naming conventions are not stable across FYAsset, and legacy-era spelling/style drift is still visible in core surfaced types and constants

Files:
- `Assets/FYAsset/Scripts/FYAssetConstants.cs:10`
- `Assets/FYAsset/Scripts/FYAssetConstants.cs:27`
- `Assets/FYAsset/Scripts/FYAssetConstants.cs:52`
- `Assets/FYAsset/Scripts/Helpers/ScriptObjectDataBse.cs:5`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:13`

What is inconsistent:
- constant naming mixes several styles:
  - `PROJECTNAME`
  - `HOTFIX_URL`
  - `AA_LABELS_CONFIG_ASSETPATH`
  - `BUILD_INDEX_JSON_PROJECT_PATH`
- `DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL` contains a spelling error in a public constant name.
- `ScriptObjectDataBse` is a typo in a concrete class name.
- `allEntries`, `keysByType`, `keysByLabel` in `AddressableLabelsConfig` use lower camelCase public fields while newer FYAsset models often use PascalCase public fields.
- directory names like `BuildManage` and `HelperBuildData_Remote` do not match the cleaner naming style used in newer FYAsset areas.

Why this matters:
- these are not just cosmetic issues when they appear in public types, paths, and constants.
- they establish uncertainty about what the naming rule actually is.
- once a subsystem stops having a stable naming grammar, every new addition requires local judgment instead of following a system.

Recommendation:
- define one explicit FYAsset naming policy for:
  - constants,
  - serialized public fields,
  - type names,
  - folder names.
- then fix only the highest-surface mistakes first:
  - `DEAULT_*`
  - `ScriptObjectDataBse`
  - the most visible legacy folders/types that developers still touch.

### [P1] `IPackageBackend` exists, but the real backend boundary is still porous and semantically under-specified

Files:
- `Assets/FYAsset/Scripts/Interfaces/IPackageBackend.cs:15`
- `Assets/FYAsset/Scripts/Interfaces/IPackageBackend.cs:35`
- `Assets/FYAsset/Scripts/Interfaces/IPackageBackend.cs:49`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:362`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:378`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:400`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:201`

What is wrong with the boundary:
- `IPackageBackend` nominally abstracts package loading/unloading.
- but it now includes default entry-id overloads and `UnloadByEntryId()` as partial escape hatches.
- `AssetPackageManager` still type-checks `ABPackageBackend` explicitly and carries AB-specific handling branches.

That means the interface is not fully expressing the real semantics needed by the system.

Why this matters:
- when an interface exists but the caller still needs concrete type knowledge, the boundary is misleading.
- maintainers may overestimate interchangeability of backends.
- future backend work is harder because the actual contract is split between:
  - interface methods,
  - default methods,
  - manager-side casts,
  - concrete backend extras.

Recommendation:
- either make `IPackageBackend` truly minimal and keep advanced behavior outside it;
- or formally enrich the boundary to represent resolved-entry loading and release semantics explicitly.

Current state is in-between, which is the hardest state to maintain.

### [P1] `IAssetIndex` also mixes canonical API and compatibility API in one interface, so the abstraction boundary communicates less than it should

Files:
- `Assets/FYAsset/Scripts/Interfaces/IAssetIndex.cs:13`
- `Assets/FYAsset/Scripts/Interfaces/IAssetIndex.cs:28`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:11`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:15`

What is happening:
- `IAssetIndex` contains both:
  - legacy string-key query methods;
  - new `RuntimeAssetEntry`-centric methods.
- old implementation (`AddressableLabelsConfig`) only meaningfully supports the first half.
- new implementation (`ABAssetIndex`) supports both.

Why this matters:
- the interface name sounds canonical, but the actual contract is transitional.
- default methods that throw `NotSupportedException` are a warning sign here: they indicate the abstraction is spanning incompatible capability sets.

Recommendation:
- separate "legacy string query index" from "entry-aware resolve index", even if one temporarily adapts into the other.
- that will make call sites easier to reason about and stop compatibility logic from dictating the shape of the long-term API.

### [P1] Runtime-side naming is generally cleaner than legacy/build-side naming, but that difference itself is now a maintainability problem

Files:
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeMessage.cs:19`
- `Assets/FYAsset/Scripts/Runtime/Models/ResolveResult.cs`
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeAssetEntry.cs`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/PackageEntry.cs`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/VersionState.cs`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/Manifest.cs`

Observation:
- newer runtime classes tend to use clearer domain naming:
  - `RuntimeMessage`
  - `ResolveResult`
  - `RuntimeAssetEntry`
- some older helper/build-side classes remain generic or ambiguous:
  - `Manifest`
  - `PackageEntry`
  - `BundleInfo`
  - `VersionState`

Why this matters:
- names that are sufficiently clear within one local folder can still be globally weak in a growing subsystem.
- once there are multiple manifests, multiple package entries, and multiple version notions, generic names stop scaling.

Recommendation:
- prioritize disambiguating "generic noun" types in legacy/build helper areas when they are next touched.
- aim for names that remain clear outside their immediate folder, not just inside it.

### [P2] Message/result object design is partially consistent, but not yet standardized enough to be a reusable subsystem pattern

Files:
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeMessage.cs:57`
- `Assets/FYAsset/Scripts/Build/Editor/BuildMessage.cs:65`
- `Assets/FYAsset/Scripts/Runtime/Models/ResolveResult.cs:11`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResult.cs:8`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/ScanResult.cs:8`

What is good:
- `RuntimeMessage` and `BuildMessage` both use:
  - code enum/constant families,
  - severity,
  - private construction plus semantic factories.

What is inconsistent:
- `ResolveResult`, `BuildTaskResult`, `BuildResult`, `BuildVerificationResult`, and `ScanResult` do not all follow the same style.
- some are factory-based immutable-ish carriers;
- some are open mutable field bags;
- some express failure as structured message;
- others use separate booleans, counts, and string fields.

Why this matters:
- "result objects" are one of the most copied patterns in a subsystem.
- if they do not share a consistent shape, every new result type becomes a style decision instead of a reuse decision.

Recommendation:
- define a small FYAsset convention for result/message carriers:
  - when to use factory methods,
  - when mutable fields are acceptable,
  - whether a result embeds a message object or raw strings,
  - how success/failure is represented.

### [P2] Public field style is inconsistent between config/data objects, which weakens predictability

Files:
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:16`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:30`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:13`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/AddressableLabelsConfig.cs:16`
- `Assets/FYAsset/Scripts/Helpers/ScriptObjectDataBse.cs:9`

What is inconsistent:
- collector/build pipeline config models often use PascalCase public fields:
  - `Packages`
  - `PackageName`
  - `Groups`
- some legacy helper/config models use lower camelCase public fields:
  - `allEntries`
  - `keysByType`
  - `keysByLabel`
  - `groups`

Why this matters:
- serialized field naming is visible in editors, code, reviews, and migration utilities.
- once mixed styles coexist for long enough, no one knows which one to use in new types.

Recommendation:
- explicitly choose whether FYAsset serialized public fields should be PascalCase or camelCase.
- then apply that rule consistently to new code and opportunistically normalize touched legacy types.

### [P2] Folder boundaries expose project history more than current architectural intent

Files:
- `Assets/FYAsset/Scripts/LegacyRuntime/`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/`
- `Assets/FYAsset/Scripts/Build/BuildManage/`
- `Assets/FYAsset/Scripts/Build/BuildManage/HelperBuildData_Remote/`

Why this matters:
- names like `LegacyRuntime`, `Compatibility`, `BuildManage`, `HelperBuildData_Remote` are understandable individually.
- together, they suggest history-driven organization more than intention-driven organization.
- that makes the module map harder to scan:
  - what is truly runtime?
  - what is transitional compatibility?
  - what is legacy but still authoritative?
  - what is helper versus product logic?

Recommendation:
- when larger refactors happen, prefer boundary names that answer responsibility directly:
  - runtime loading,
  - hotfix transport,
  - build export,
  - compatibility adapters,
  - legacy support.

This is not a request for immediate folder churn. It is a maintainability direction.

### [P2] Terminology around "key", "address", "entryId", "type", and "label" is better than before, but the interfaces still blur those concepts in places

Files:
- `Assets/FYAsset/Scripts/Interfaces/IPackageBackend.cs:15`
- `Assets/FYAsset/Scripts/Interfaces/IAssetIndex.cs:17`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:116`
- `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:292`

Why this matters:
- FYAsset actually has distinct concepts:
  - `Address`
  - `EntryId`
  - `TypeKey`
  - `Label`
  - legacy generic `key`
- but `IPackageBackend` and some manager APIs still expose the generic word `key` where the actual semantics are narrower or context-dependent.

Recommendation:
- keep generic `key` only where the boundary is truly backend-neutral and unavoidably abstract.
- otherwise prefer semantic names at API boundaries, because naming is part of interface design, not just readability.

### [P3] Some files still miss the basic documentation/context level that newer FYAsset files already follow

Files:
- `Assets/FYAsset/Scripts/Interfaces/IPackageBackend.cs:5`
- `Assets/FYAsset/Scripts/Runtime/Backends/Addressables/AddressablesBackend.cs:8`
- `Assets/FYAsset/Scripts/Helpers/ScriptObjectDataBse.cs:5`

Why this matters:
- many newer FYAsset files have strong top-of-file comments describing design intent and usage.
- these files are comparatively sparse, which makes them feel older and more incidental even when they are still important.

Recommendation:
- bring high-surface legacy files up to the current documentation baseline when touched:
  - what boundary they represent,
  - what semantics terms like `key` mean,
  - what invariants callers should rely on.

## Positive notes

There are clear signs that FYAsset is already converging toward a better style:

- `RuntimeMessage` and `BuildMessage` show deliberate error-model design.
- `RuntimeAssetEntry`, `ResolveResult`, `ABManifest`, and the collector rule context types are much easier to reason about than older helper-era types.
- newer comments are generally strong and explain intent, not just mechanics.

That is useful because it means the subsystem already contains the style worth standardizing around.

## Suggested cleanup order

1. Standardize high-surface naming.
   Start with obvious typos and the most user-visible legacy constants/types.

2. Clarify boundary contracts.
   Split transitional interfaces from canonical ones instead of hiding capability mismatches behind default methods.

3. Standardize result/message object patterns.
   Reuse the good parts of `RuntimeMessage` / `BuildMessage` more broadly.

4. Normalize serialized field style for new code.
   Then opportunistically migrate touched legacy files.

5. Treat folder names and generic nouns as long-term maintenance debt, not urgent churn.
   Rename only when already doing substantive work in that area.

## Overall assessment

FYAsset's naming and maintainability story is directionally improving, but the subsystem still exposes too much of its migration history. The main problem is not that any one name is terrible. It is that the system does not yet present a single, confident architectural vocabulary.

That shows up as:
- inconsistent naming grammar,
- interfaces that are more transitional than they look,
- similar concepts modeled with different patterns depending on age.

The next quality jump will come from standardizing the language of the subsystem, not just adding more abstractions.

Signature: `gpt-5.4/codex`
