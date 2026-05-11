# Full Review — Landed Refactor Code (Detailed Findings)

> **Date**: 2026-05-02
> **Reviewer**: Sisyphus
> **Scope**: Landed refactor code across runtime asset loading, AB/Legacy backend split, manifest/index loading, handle lifecycle, and differential build tooling
> **Method**: Static code review, targeted file inspection, repository search, and architecture consultation
> **Processed**: 2026-05-11 · Findings addressed in `34e002b` + `a1aff30` fix rounds
> **Status**: 📦 Archived

---

## Reviewed Areas

- `AssetPackageManager`
- `ABPackageBackend`
- `ABBundleLoader`
- `ManifestLoader`
- `AddressablesBackend`
- `ABAssetIndex`
- `HandleRegistry`
- `AssetHandle<T>`
- `DifferentialProcessor`

---

## P1 — Should Fix Before Relying on This Path in Production

### P1-1: AB initialization failure does not produce a strong manager state contract

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:26-47`
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:56-77`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ManifestLoader.cs:30-75`
- **Problem**:
  - `AssetPackageManager.Initialize()` selects the AB path when `FYAssetConstants.USE_AB_BACKEND` is enabled.
  - `InitializeWithABIndex()` logs and returns if `ManifestLoader.LoadAsync()` fails.
  - The caller only gets `_index == null`, `_isInitialized == false`, and later runtime logs such as `AssetPackageManager 未初始化`.
- **Impact**:
  - Startup failure is visible, but not operationally strong enough.
  - Higher-level systems do not get an explicit fatal contract, recovery policy, or diagnostics object.
  - This is especially risky because AB mode is an all-or-nothing runtime path.
- **Assessment**:
  - The code is readable, but the failure mode is too soft for a critical bootstrap step.
- **Recommendation**:
  - Choose one explicit contract and enforce it:
    1. **Fail-fast contract**: throw / expose fatal state / stop the game flow clearly, or
    2. **Supported fallback contract**: intentionally fall back to Legacy and emit a structured warning.
  - Do not keep the current middle ground where the manager simply stays uninitialized.

### P1-2: Duplicate-address support is undermined by nondeterministic unload behavior

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:49-50`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:179-194`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:234-257`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:410-457`
- **Problem**:
  - The backend stores `address -> HashSet<EntryId>`.
  - `UnloadAsset(string key)` picks the first `EntryId` from the hash set and releases it.
  - `ResolveAssetEntryByAddress()` also uses a first-match policy for address-only lookup.
- **Impact**:
  - The project explicitly allows duplicate Address values, but address-based unload does not preserve identity.
  - In practice, a caller may unload the wrong asset/bundle pair.
  - This creates a correctness hole exactly where the refactor claimed to improve identity semantics through `EntryId`.
- **Assessment**:
  - This is not just a legacy compatibility quirk. It directly weakens one of the core refactor goals.
- **Recommendation**:
  - Make `EntryId` the only supported unload identity for AB mode, or
  - Make address-based unload deterministic and well-documented, with tests proving the chosen rule.
  - The current "first item in `HashSet`" behavior should not remain.

---

## P2 — Important But Not Immediate Blockers

### P2-1: Lifetime management is split across multiple classes with an implicit protocol

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:14-20`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:177-178`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:197-203`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:432-457`
  - `Assets/FYAsset/Scripts/Runtime/Models/HandleRegistry.cs:63-67`
  - `Assets/FYAsset/Scripts/Runtime/Models/HandleRegistry.cs:173-238`
  - `Assets/FYAsset/Scripts/Runtime/Models/AssetHandle.cs:120-143`
- **Problem**:
  - `AssetHandle<T>` calls into `HandleRegistry`.
  - `HandleRegistry` decides when an `EntryId` reaches zero active handles.
  - Only then does it invoke the backend release callback, which finally calls `ABBundleLoader.UnloadBundle()`.
  - The protocol is sensible, but it is distributed rather than encapsulated.
- **Impact**:
  - The correctness of bundle release depends on multiple components remaining perfectly aligned.
  - Future changes can easily break this without immediately obvious compiler errors.
- **Assessment**:
  - Cohesion is improved compared with older code, but ownership is still not fully localized.
- **Recommendation**:
  - Add invariants/tests around this contract at minimum.
  - Long-term, consider whether the backend should own more of the final asset-level release logic directly.

### P2-2: Async load paths do not deduplicate in-flight work

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/Addressables/AddressablesBackend.cs:24-43`
  - `Assets/FYAsset/Scripts/Runtime/Backends/Addressables/AddressablesBackend.cs:84-91`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:93-107`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:113-126`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:270-287`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:321-365`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:181-241`
- **Problem**:
  - Cache lookup happens before the async work starts.
  - If two callers request the same asset before the first path populates the cache, both loads proceed.
  - The same pattern exists in both the Addressables and AB implementations.
- **Impact**:
  - Redundant I/O and extraction work.
  - Avoidable race windows around cache population and refcount interpretation.
  - Harder-to-reason-about behavior under stress or future parallelization.
- **Assessment**:
  - This is a classic “works in normal flow, degrades under concurrency” issue.
- **Recommendation**:
  - Introduce an in-flight map (`key -> Task` or `EntryId -> Task`) so concurrent callers await the same operation.

### P2-3: Unity-thread assumptions in Task-based async code are still mostly implicit

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:181-241`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:317-347`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABBundleLoader.cs:447-458`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ManifestLoader.cs:81-107`
  - `Assets/FYAsset/Scripts/Runtime/Models/HandleRegistry.cs:17-19`
- **Problem**:
  - The runtime uses `Task` wrappers around Unity async primitives.
  - The code comments assume main-thread execution, but the enforcement is mostly conventional rather than explicit.
- **Impact**:
  - The current path may be safe in the expected launcher flow, but the contract is fragile if reused from a different synchronization context later.
- **Assessment**:
  - This is more of an operational hardening issue than a confirmed bug.
- **Recommendation**:
  - Document the expected call context clearly and add assertions / test coverage where feasible.

---

## P3 — Cleanup, Consistency, and Performance Follow-Up

### P3-1: `ABAssetIndex` uses mixed lookup semantics that should be documented or unified

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:34-35`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:75-106`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:134-156`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABAssetIndex.cs:198-225`
- **Problem**:
  - Labels are indexed with `StringComparer.OrdinalIgnoreCase`.
  - Address and type lookups are not normalized the same way.
- **Impact**:
  - This can become a contract trap for callers if they assume all runtime lookups behave uniformly.
- **Recommendation**:
  - Either unify the lookup policy or explicitly document which dimensions are case-sensitive and why.

### P3-2: `DifferentialProcessor` still performs expensive editor work in a very chatty per-item style

- **Files**:
  - `Assets/FYAsset/Scripts/Build/BuildManage/Editor/DifferentialProcessor.cs:46-59`
  - `Assets/FYAsset/Scripts/Build/BuildManage/Editor/DifferentialProcessor.cs:251-315`
  - `Assets/FYAsset/Scripts/Build/BuildManage/Editor/DifferentialProcessor.cs:321-364`
- **Problem**:
  - The code performs per-asset deep hash generation, per-asset group moves, and per-asset logs.
  - This is acceptable at current scale but will become noisier and slower as the project grows.
- **Impact**:
  - Editor productivity and build preparation latency will degrade before runtime performance does.
- **Recommendation**:
  - Keep the current behavior for correctness, but plan a later pass for batched logging and more measured hashing/move costs.

### P3-3: Startup telemetry is readable but still not ideal for diagnostics and tooling

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:58-63`
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:73-76`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ManifestLoader.cs:68-73`
- **Problem**:
  - Logs are human-readable, but there is no stronger structured health reporting for startup state.
- **Impact**:
  - When diagnosing field issues, logs alone may be enough for a developer but weaker for automation, launch gates, or QA tooling.
- **Recommendation**:
  - Consider exposing a small startup status/report object or health check entry point in addition to logs.

---

## System-Level Assessment

### Code Quality

- Overall quality is solid in the runtime refactor area.
- Comments are abundant and mostly useful.
- The main weakness is not messy code; it is that a few critical runtime contracts remain softer than the surrounding architecture suggests.

### Architecture

- The AB-vs-Legacy split is now credible and easy to follow.
- `AssetPackageManager` still works as a compatibility facade, which keeps migration risk under control.
- The current architecture does not need redesign; it needs stronger behavioral guarantees.

### Cleanliness

- The runtime side is cleaner than the editor build side.
- `ABAssetIndex`, `RuntimeMessage`, and `BuildMessage` are especially clean.
- The main cleanliness debt is hidden contract coupling, not formatting or naming.

### Coupling / Cohesion

- Runtime cohesion improved substantially.
- The remaining coupling is mostly lifecycle coupling: handle state, backend release, and bundle release are still coordinated across layers.

### Performance

- Runtime hot paths are generally acceptable for the current stage.
- The immediate performance concern is concurrency duplication in async load paths.
- The next performance concern is editor-side cost in `DifferentialProcessor`, not runtime indexing.

---

## Recommended Next Fix Order

1. Make AB initialization failure explicit and operationally strong.
2. Remove nondeterministic address-based unload behavior.
3. Add in-flight load dedupe for both backends.
4. Add focused tests for duplicate address, repeated handle release, and concurrent load scenarios.
5. Tighten thread/context assumptions and later revisit editor-side performance.

---

## Final Assessment

The landed codebase is in a better state than the earlier refactor snapshots. The major win is that the runtime architecture now has understandable boundaries instead of only future intent. The remaining issues are narrow but important: startup failure semantics, duplicate-address identity handling, and implicit lifetime/concurrency contracts.

That is a good review outcome. It means the system is close to being dependable, not far from being viable.
