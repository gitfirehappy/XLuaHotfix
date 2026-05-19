# Sub-Plan B5-4: Migration Path & Legacy API Deprecation Strategy

> **Risk**: Medium
> **Dependencies**: B5-1 + B5-2 + B5-3 approval completed
> **Status**: Archived — 2026-05-19; Deferred indefinitely — migration strategy evolves with implementation (decision: 2026-04-07)

---

## Objective

Clarify the B5 implementation order:

- What to add first
- What to change later
- When legacy interfaces are compatibility-preserved, when deprecated
- Which modules serve as first-batch migration targets

Incrementally migrate `AssetPackageManager` from 'string key driven' to 'Resolve + AssetHandle driven' without breaking existing runtime behavior.

---

## Background

The project has completed B1 / B2 / B3, meaning:

- Abstraction layers are already properly separated
- But most runtime calls still use the old `LoadAssetAsync<T>(key)` / `UnloadAsset(key)` mental model

Cutting the old API in one shot carries high risk;
long-term dual-track coexistence would cause continuous call-site fragmentation.

Therefore this sub-plan specifically defines migration cadence and deprecation conditions.

---

## Confirmed Rules

1. New API must run successfully before migrating old API
2. New API must support both Sync / Async from the start
3. Legacy `LoadAssetAsync<T>(key)` first maps to `LoadByAddress`
4. `Handle-first` is the target direction; cannot regress to long-term string-based unloading
5. B4 is not in this round's execution scope; migration plan must not smuggle in hotfix core pipeline changes

---

## Planned Tasks

### Task 1: Plan Addition Phase

- Define the introduction order for `ResolvedEntry` / `AssetHandle<T>` / new Resolve / Load APIs
- Define which layer the old API wrapper should reside in
- Define which interfaces initially preserve compatibility shells

### Task 2: Plan Replacement Phase

- Define first-batch migration call sites
- Define when to migrate `UnloadAsset(string key)` related calls
- Define the bridging timing between batch `ByLabel(s)` interfaces and new batch APIs

### Task 3: Plan Deprecation Phase

- Define the timing for marking legacy APIs `Obsolete`
- Define when compatibility shells can be deleted
- Define observation criteria for verifying new API stability

---

## Preservation Requirements (Must Pass)

- [x] Do not delete legacy API before new API is verified
- [x] Migration phase must not simultaneously push B4 high-risk pipeline changes
- [x] Compatibility layer behavior must be transparent; must not make excessive implicit guesses on failure
- [x] Migration plan must clearly state 'verify first, then replace' rather than full cutover

---

## Acceptance Criteria

- [ ] Addition, replacement, and deprecation — all three phase boundaries are clear
- [ ] Can explain which call sites migrate first, which later, which must wait for batch API finalization
- [ ] The destination for `LoadAssetAsync<T>(key)`, `LoadAssetSync<T>(key)`, `UnloadAsset(string key)` is clear
- [ ] Migration plan does not smuggle in B4, RawFile, or other undefined scope items

---

## Out of Scope

- Concrete `ABPackageBackend` / `ABAssetIndex` implementation
- B4's catalog / locator replacement
- RawFile API migration

---

## Approval Checklist

- [x] Does migration insist on 'new API runs successfully before migrating old API'?
  **Decision**: Yes.
- [x] Does new API support Sync / Async from the start?
  **Decision**: Yes.
- [x] Does legacy `LoadAssetAsync<T>(key)` first map to `LoadByAddress`?
  **Decision**: Yes.
- [x] First batch replacement call sites: start from `AssetPackageManager` shell, `XLuaLoader`, or other modules?
  **Decision**: AssetPackageManager internals first. It's the unified entry point for all callers; changing internal implementation is transparent to external code, making it the best position to verify the new API.
- [x] Do legacy `LoadAssetByLabel(s)` / `UnloadAssetByLabel(s)` wait for B5-2 batch API finalization before migrating?
  **Decision**: Yes. First batch migration does single-asset paths (ByAddress / ByTypeKey); batch paths wait for ResolveMany + LoadMany + LoadByLabels implementation.
- [x] At which phase should `UnloadAsset(string key)` be marked `Obsolete`?
  **Decision**: Synced with B5-2 decision — after the first batch of call sites is migrated.
