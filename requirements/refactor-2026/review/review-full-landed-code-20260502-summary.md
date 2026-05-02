# Full Review — Landed Refactor Code (Summary)

> **Date**: 2026-05-02
> **Reviewer**: Sisyphus
> **Scope**: Landed refactor code across runtime asset loading, AB/Legacy backend split, manifest/index loading, handle lifecycle, and differential build tooling
> **Method**: Static code review, targeted file inspection, repository search, and architecture consultation

---

## Review Status

- This report reflects the code inspected on 2026-05-02.
- It is based on direct source review plus one completed Oracle architecture review.
- Two low-level background exploration tasks were still running when this draft was written; if they surface anything materially new, a small addendum should be added rather than replacing this report.

---

## Findings Summary

| Severity | Count | Focus |
|----------|-------|-------|
| P1 | 2 | Startup correctness, duplicate-address unload correctness |
| P2 | 3 | Lifetime contract clarity, async concurrency, Unity thread assumptions |
| P3 | 3 | Index consistency, editor-side performance, maintainability/telemetry |

---

## What Improved

The current landed refactor is directionally strong:

- The runtime path is much clearer than the legacy shape: `AssetPackageManager -> IAssetIndex / IPackageBackend -> AB or Addressables implementation`.
- `RuntimeMessage` / `BuildMessage` provide a cleaner structured error surface than the old ad-hoc style.
- `ABAssetIndex` is compact, readable, and keeps the hot query path straightforward.
- `ABBundleLoader` now contains an explicit Android `StreamingAssets` branch, so the earlier cross-platform bundle-loading risk has been reduced.
- `HandleRegistry` + `AssetHandle<T>` form a coherent zero-GC ownership model for the new runtime API.

So the refactor is not failing because of the overall architecture. The remaining issues are mostly contract-tightening and correctness hardening.

---

## Highest-Priority Risks

### P1-1: AB startup can leave `AssetPackageManager` in an unusable half-initialized state

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:26-47`
  - `Assets/FYAsset/Scripts/Runtime/Compatibility/AssetPackageManager.cs:56-77`
- **Why it matters**:
  - If `FYAssetConstants.USE_AB_BACKEND` is enabled and `ManifestLoader.LoadAsync()` returns `null`, initialization exits early.
  - The manager then remains not initialized, but the failure is only logged rather than converted into an explicit fatal state or actionable recovery contract.
- **Priority**: Fix first.

### P1-2: Address-based unload is unsafe when multiple entries share the same Address

- **Files**:
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:49-50`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:179-194`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:234-257`
  - `Assets/FYAsset/Scripts/Runtime/Backends/AB/ABPackageBackend.cs:410-457`
- **Why it matters**:
  - `UnloadAsset(string key)` selects the first `EntryId` from a `HashSet<string>` and releases that one.
  - This violates the project's own duplicate-address direction and makes address-based unload nondeterministic.
- **Priority**: Fix immediately after startup hardening.

---

## Important Non-Blocking Issues

### P2-1: The lifetime contract between `HandleRegistry` and `ABPackageBackend` is correct in intent but too implicit

- The current design works only if every caller obeys the exact release protocol.
- That protocol is spread across comments and separate classes rather than enforced in one place.

### P2-2: Async load paths do not deduplicate in-flight requests

- Concurrent loads for the same key can perform repeated work and create avoidable race windows in caches/refcounts.

### P2-3: Unity main-thread assumptions are still mostly implicit in `Task`-wrapped async paths

- The code likely works in the intended execution context, but that contract is not explicit or guarded.

---

## Lower-Priority Cleanup / Polish

### P3-1: `ABAssetIndex` uses mixed matching semantics

- Label lookup is case-insensitive, while address/type indexes are not normalized in the same way.
- This is not necessarily wrong, but it is a contract sharp edge that should be documented or unified.

### P3-2: `DifferentialProcessor` still pays for heavy per-asset editor work

- Per-entry group moves, per-entry logs, and deep hash generation are acceptable for now, but this will become a productivity bottleneck as asset counts grow.

### P3-3: Startup telemetry is informative but not operationally strong enough

- Current logs are readable, but they do not expose a stronger health state or recovery path for callers/tools.

---

## Priority Order

1. Harden AB startup failure handling in `AssetPackageManager`.
2. Remove or redesign nondeterministic address-based unload in `ABPackageBackend`.
3. Add in-flight load dedupe for AB and Addressables backends.
4. Make lifetime ownership rules more explicit and testable.
5. Clean up thread/context assumptions and editor-side performance hotspots.

---

## Verdict

The landed refactor is structurally good and worth continuing. The runtime split is cleaner than before, and the project now has a credible custom AB path instead of only a conceptual one. The main risk is no longer architectural confusion; it is that a few runtime contracts are still underspecified at the exact points where production systems usually fail: startup, duplicate-identity handling, and ownership release.

This means the recommendation is **not** to roll back or redesign. It is to keep the current architecture and tighten the operational contracts around it.
