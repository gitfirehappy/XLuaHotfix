# FYAsset Data Structure Review

Date: 2026-05-08
Scope: `Assets/FYAsset`
Focus: review the supporting completeness of already-landed important data structures, especially value semantics, equality/hash behavior, mutable exposure, string/allocation patterns, and diagnostic friendliness.
Author: `gpt-5.4/codex`
**Processed**: 2026-05-11 · Structural issues (IReadOnlyList exposure, zero-alloc claims) addressed in `34e002b` + `a1aff30`. Value-type equality contracts partially addressed.
**Status**: 📦 Archived

## Summary

This review did not find one single catastrophic bug concentrated in one file, but it did find a recurring pattern: several important FYAsset data structures already carry real business semantics, while their supporting facilities are still uneven. The most notable gaps are:

1. some `struct` types already behave like semantic value objects, but do not explicitly define equality/hash/to-string contracts;
2. several central model classes expose mutable collections directly while simultaneously introducing caches or indexes that assume those collections stay stable;
3. some index/query APIs claim "zero allocation" or "read-only view", but still allocate fresh arrays/lists on each call, making the contract easy to misuse later.

Below are the concrete findings, ordered by severity.

## Findings

### [P1] `RuntimeAssetEntry` caches normalized labels, but the type still exposes `Labels` as a mutable public list with no safe mutation boundary

Files:
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeAssetEntry.cs:42`
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeAssetEntry.cs:74`
- `Assets/FYAsset/Scripts/Runtime/Models/RuntimeAssetEntry.cs:95`
- `Assets/FYAsset/Scripts/Runtime/Models/ManifestAssetEntry.cs:96`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:63`

Why this matters:
- `RuntimeAssetEntry` introduces `_normalizedLabelsCache` and documents that callers must manually invoke `InvalidateLabelCache()` when `Labels` changes.
- At the same time, `Labels` is still a public `List<string>`, so the type cannot enforce cache invalidation.
- This creates a silent stale-cache hazard: once `HasAllLabels()` or `GetNormalizedLabels()` is called, any later direct mutation to `Labels` can produce inconsistent query results.

Why I consider this a real engineering issue instead of a style nit:
- the type has already moved beyond "plain DTO" territory by adding cached derived state;
- `ManifestAssetEntry.ToRuntimeEntry()` creates a mutable list copy, and `ABAssetIndex` stores and reuses the resulting `RuntimeAssetEntry` instances, so stale cached state can persist for the lifetime of the index.

Recommendation:
- either keep the type as a pure dumb DTO and remove the cache;
- or promote it into a guarded model type:
  - make `Labels` private/set-once or expose `IReadOnlyList<string>`;
  - add controlled mutation helpers such as `SetLabels`, `AddLabel`, `RemoveLabel`;
  - invalidate cache internally instead of relying on caller discipline.

### [P1] `ABAssetIndex` and `ABManifest` query APIs still allocate fresh result containers, but comments describe them as cached/read-only views or low-allocation hot paths

Files:
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:10`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:196`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:203`
- `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:218`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:183`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:191`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:214`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:222`
- `Assets/FYAsset/Scripts/Runtime/Models/ABManifest.cs:275`

Why this matters:
- `ABAssetIndex` comments say query methods return cached `RuntimeAssetEntry` references and even mention "zero allocation hot path".
- In practice:
  - `GetEntriesByAddress()` allocates a new array every call;
  - `GetEntriesByAddressAndType()` allocates a new list every call;
  - `ABManifest.TryGetAssetsBy*()` allocates a new list every call;
  - `ABManifest.GetDirectDependencies()` allocates a new list every call.

This is not functionally wrong, but it is a contract drift problem:
- maintainers reading the comments may place these methods on hot paths under a false assumption;
- later optimization work becomes harder because callers may already depend on current allocation behavior.

Recommendation:
- choose one direction and make code/comments match:
  - if low-allocation is the goal, return cached index slices, pooled lists, or custom read-only views;
  - if simplicity is the goal, soften the comments and explicitly document per-call allocation.

### [P1] `CollectorReverseIndex.CollectorRef` is effectively a key/value-object-style struct but has no explicit equality/hash contract

Files:
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:10`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectorReverseIndex.cs:16`

Why this matters:
- `CollectorRef` semantically represents a stable logical location: `(PackageIndex, GroupIndex, CollectorIndex)`.
- Today it is used as a dictionary value, so default struct equality does not break current behavior.
- But this is exactly the kind of important "small semantic struct" that usually grows into:
  - HashSet membership,
  - diff/comparison logic,
  - undo/redo snapshots,
  - diagnostics.

Without explicit `IEquatable<CollectorRef>`, `Equals(object)`, `GetHashCode()`, and ideally `ToString()`, future use is fragile and harder to reason about.

Recommendation:
- treat `CollectorRef` as a first-class value object and add the standard value-type support set.

### [P1] `AssetClassification` is a semantic value struct consumed across classifier/group/pack boundaries, but still lacks explicit value-object facilities

Files:
- `Assets/FYAsset/Scripts/Build/Collector/AssetClassification.cs:8`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/Rules/IGroupRule.cs:18`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/Rules/IPackRule.cs:28`

Why this matters:
- `AssetClassification` already carries a meaningful cross-stage contract: `Role + PayloadKind`.
- It is passed through rule contexts and stored on `CollectedAssetInfo`, so it is no longer just a temporary tuple.
- The current struct is technically valid, but it is missing the normal support surface for a semantic struct:
  - `IEquatable<AssetClassification>`
  - `Equals`
  - `GetHashCode`
  - `ToString`

That omission makes later debugging and set/dictionary usage less robust than it should be.

Recommendation:
- implement full value semantics now, while the type is still small.

### [P2] `BundleDownloadItem` has the same completeness problem as other semantic structs

Files:
- `Assets/FYAsset/Scripts/LegacyRuntime/IHotfixPipeline.cs:75`

Why this matters:
- `BundleDownloadItem` is not a random transport blob anymore. It is the normalized cross-backend unit for hotfix download planning.
- It will naturally appear in comparisons, logs, dedup steps, and test assertions.
- Right now it has no explicit equality/hash/string contract.

Recommendation:
- same as above: treat it as a value object and add `IEquatable<BundleDownloadItem>`, `GetHashCode`, and `ToString`.

### [P2] Several central collector/build model classes have crossed beyond "pure DTO" usage, but still expose all fields and collections publicly

Files:
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:16`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:33`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:56`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:59`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:97`
- `Assets/FYAsset/Scripts/Build/Collector/CollectorSetting.cs:100`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectedAssetInfo.cs:13`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResult.cs:11`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildVerificationResult.cs:9`
- `Assets/FYAsset/Scripts/Runtime/Models/ManifestBundleEntry.cs:52`
- `Assets/FYAsset/Scripts/Runtime/Models/ManifestBundleEntry.cs:71`
- `Assets/FYAsset/Scripts/Runtime/Models/ManifestBundleEntry.cs:80`

Why this matters:
- Public fields are common and acceptable in Unity serialization models.
- The issue here is not "must use properties", but that these types now carry invariants:
  - `BuildTaskResult` is supposed to be constructed through factory methods;
  - `BuildVerificationResult` stores derived counts that must stay aligned with `Issues`;
  - `ManifestBundleEntry` has runtime-populated collections that should reflect `ABManifest.Initialize()` output;
  - collector hierarchy objects are used by validators, scanners, UI and reverse indices simultaneously.

Once a type has invariants, unrestricted mutation becomes a maintainability risk.

Recommendation:
- for pure serialized config types, keep public fields if needed, but isolate invariant-bearing runtime/build-result types behind constructors/factories or limited setters;
- at minimum, document which classes are intended as mutable authoring DTOs and which ones are post-build/read-only runtime snapshots.

### [P2] `BuildContext.Get<T>/Require<T>` rely on raw cast semantics without any guardrails around type mismatch

Files:
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildContext.cs:14`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildContext.cs:20`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildContext.cs:28`

Why this matters:
- `BuildContext` is the central typed KV bus for the build DAG.
- A wrong type read currently throws an `InvalidCastException` at the cast site, with little contextual help.
- For an internal build graph, that is survivable, but not ideal given this object is the backbone for task composition.

Recommendation:
- add `TryGet<T>(string key, out T value)` and a clearer mismatch exception path such as:
  - required key exists but stored type is `X`, requested `Y`.

This is not a correctness bug today, but it is a missing support facility for a foundational structure.

### [P2] `ScanResult.HasErrors` uses LINQ while the surrounding FYAsset codebase repeatedly optimizes away LINQ in similar model/query paths

Files:
- `Assets/FYAsset/Scripts/Build/Collector/Editor/ScanResult.cs:2`
- `Assets/FYAsset/Scripts/Build/Collector/Editor/ScanResult.cs:23`

Why this matters:
- This is editor-side code, so the performance impact is small.
- The bigger issue is consistency: much of FYAsset explicitly avoids LINQ in data-path code, yet this result type reintroduces it for a trivial scan.
- That makes style and allocation expectations less predictable across the subsystem.

Recommendation:
- either keep LINQ consistently for editor-only result wrappers, or replace this with a simple loop and keep the "no LINQ in core data structures" rule consistent.

### [P3] `AssetHandle<T>` is an important struct but still lacks explicit equality/hash behavior, which weakens its value-type contract

Files:
- `Assets/FYAsset/Scripts/Runtime/Models/AssetHandle.cs:25`
- `Assets/FYAsset/Scripts/Runtime/Models/AssetHandle.cs:175`

Why this matters:
- `AssetHandle<T>` is intentionally a value type and carries identity through `(HandleId, Generation)`.
- It already has `ToString()`, which is good.
- But it still lacks explicit equality/hash behavior, so comparisons fall back to default struct field comparison semantics.

That fallback will usually work, but for such a central ownership token, explicit semantics would be safer and clearer.

Recommendation:
- implement `IEquatable<AssetHandle<T>>` based on `(HandleId, Generation)`;
- consider whether `_cachedAsset` and `_inlineError` should be excluded from equality semantics, because logical handle identity appears to be registry identity, not payload snapshot identity.

## Secondary observations

### String handling is generally acceptable and already shows awareness of allocation costs

Positive examples:
- `AssetHandle<T>.ToString()` uses `string.Concat`: `Assets/FYAsset/Scripts/Runtime/Models/AssetHandle.cs:177`
- `AssetResolver` uses `StringBuilder` for richer diagnostics: `Assets/FYAsset/Scripts/Runtime/Core/AssetResolver.cs:221`
- build summary generation uses `StringBuilder`: `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/TaskOrganizeOutput.cs:63`

Conclusion:
- I did not find a broad "replace all concatenation with StringBuilder" problem.
- The more important gap in FYAsset is not string concatenation style, but data-structure contract completeness.

### Equality support is currently inconsistent across the subsystem

Positive example:
- `VersionNumber` already defines `Equals`, `GetHashCode`, and operators: `Assets/FYAsset/Scripts/Build/BuildManage/VersionDataBase.cs:70`

Contrast:
- `AssetClassification`, `CollectorRef`, `BundleDownloadItem`, `AssetHandle<T>` do not.

Conclusion:
- the project already accepts the idea that important semantic types should define equality explicitly;
- the remaining missing pieces stand out more because of that precedent.

## Suggested follow-up checklist

If the goal is to harden FYAsset's important data structures systematically, I would suggest this order:

1. Define a "semantic struct baseline" and apply it to:
   - `AssetClassification`
   - `CollectorRef`
   - `BundleDownloadItem`
   - optionally `AssetHandle<T>`

2. Split FYAsset model types into two categories and document the rule:
   - mutable authoring/serialization DTOs;
   - post-build/runtime snapshot objects with controlled mutation.

3. Fix `RuntimeAssetEntry` label-cache ownership:
   - either remove cache;
   - or encapsulate label mutation.

4. Align allocation contracts with implementation in:
   - `ABAssetIndex`
   - `ABManifest`

5. Improve infrastructure ergonomics:
   - add `BuildContext.TryGet<T>()`;
   - improve type-mismatch diagnostics;
   - standardize whether editor-side result wrappers allow LINQ.

## Overall assessment

FYAsset's important data structures are already meaningful enough to deserve stronger supporting contracts than they currently have. The subsystem is not "messy"; the bigger issue is that it sits in an in-between state:

- more advanced than plain DTOs,
- but not yet fully treated as value objects / immutable snapshots / guarded runtime models.

That gap is where most of the review findings came from.
