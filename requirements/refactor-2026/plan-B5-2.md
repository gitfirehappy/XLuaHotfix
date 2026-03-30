# Sub-Plan B5-2: Resolve / Load API & AssetHandle Contract

> **Risk**: Medium
> **Dependencies**: B5-1 approval completed
> **Status**: Approved

---

## Objective

Define the final external contract for runtime Resolve / Load API, clarifying:

- Boundary between strict query and convenience query
- `AssetHandle<T>` return model
- Sync / async consistency
- Structured error model
- Compatibility mapping for legacy `LoadAssetAsync<T>(key)` and future deprecation direction

---

## Background

Once `Address` allows duplicates, the existing `LoadAssetAsync<T>(string key)` + `UnloadAsset(string key)` model has two problems:

1. **String query no longer equals unique identity**
2. **Release semantics can no longer safely rely on string key alone**

Therefore this sub-plan needs to change the runtime contract to:

- Resolve first obtains a unique entry or structured error
- Load returns `AssetHandle<T>` based on the unique entry
- Release is Handle-first

---

## Confirmed Rules

1. Resolve / Load uses dual-track semantics:
   - `ByAddress`
   - `ByTypeKey`
2. `LoadByAddress<T>` defaults to `ResolveByAddress<T>` then Load
3. `ResolveByTypeKey<T>` default contract: `Type + Key`, `Labels` optional; without `Labels`, multiple hits directly error
4. Type filtering defaults to Addressables convention: **assignable**; `Exact` only available in `Resolve API`
5. `LoadByAddressSync<T>` / `LoadByTypeKeySync<T>` maintain **fully consistent** resolve / error contract with async versions
6. Core return model is `AssetHandle<T>`
7. `AssetHandle<T>.Release()` contract: **idempotent + warn on second call**
8. Legacy `LoadAssetAsync<T>(key)` first maps to `LoadByAddress`

---

## Planned Tasks

### Task 1: Define Resolve Result Model

- Define `ResolvedEntry` field boundaries
- Define success and failure return forms for `ResolveByAddress` / `ResolveByTypeKey`
- Define candidate list and suggested filter information on conflict

### Task 2: Define AssetHandle Contract

- Define `AssetHandle<T>` minimal capabilities: `Asset / EntryId / Address / PrimaryType / IsValid / Release()`
- Clarify Handle usability constraints after release
- Clarify Handle's relationship with internal cache / ref counting

### Task 3: Define Sync / Async & Compatibility Layer

- Align sync / async entry Resolve / Load ordering
- Define compatibility mapping for legacy `LoadAssetAsync<T>(key)` / `LoadAssetSync<T>(key)`
- Prepare migration boundary for future deprecation of `UnloadAsset(string key)`

---

## Preservation Requirements (Must Pass)

- [ ] Still preserve `ByAddress` query entry; do not force all calls to switch to `ByTypeKey`
- [ ] `Exact` exists only in `Resolve API`; do not complicate everyday Load API
- [ ] New API must support both sync and async
- [ ] Legacy API must not be deleted until new API is verified

---

## Acceptance Criteria

- [ ] `ResolveByAddress` / `ResolveByTypeKey` can clearly distinguish unique hit, miss, conflict, and type mismatch scenarios
- [ ] `LoadByAddress` / `LoadByTypeKey` success and failure paths all map to structured errors or unique entries
- [ ] `AssetHandle<T>` is sufficient to serve as release identity; string key no longer used as sole unload basis
- [ ] Sync / async interfaces and compatibility layer relationships are clear, ready for implementation breakdown

---

## Out of Scope

- Batch build validation and suggested Address editor implementation
- RawFile / non-Unity asset loading interfaces
- B4's catalog / locator replacement

---

## Approval Checklist

- [x] Use `ByAddress` + `ByTypeKey` dual-track query semantics?
  **Decision**: Yes.
- [x] When `LoadByTypeKey<T>` is called without `Labels`, should multiple hits directly error?
  **Decision**: Yes; only explicit `Labels` participate in final disambiguation.
- [x] Should `Exact` capability only exist in `Resolve API`?
  **Decision**: Yes.
- [x] Should the return model center on `AssetHandle<T>`?
  **Decision**: Yes; Handle is debug-friendly, release uses idempotent + warn on second call.
- [x] For Load failure structured errors, use `Result`-style with `ErrorCode`, exception-based, or both?
  **Decision**: Result-style is primary. AssetHandle serves as the Result (IsValid + Error); add `.ThrowIfFailed()` extension method when needed, not as the primary API. Asset loading failure is an expected error, not expressed via exceptions.
- [x] Should batch `Labels` query provide both `ResolveMany + LoadMany` and direct `LoadByLabels` two-layer API?
  **Decision**: Both preserved (layered). ResolveMany + LoadMany is the lower-level capability (for validation tools and advanced scenarios); LoadByLabels is the everyday convenience wrapper (internally calls the lower level).
- [x] At which phase should legacy `UnloadAsset(string key)` be marked `Obsolete`?
  **Decision**: After the first batch of call sites is migrated. Mark Obsolete after new API is verified through first batch of call sites (e.g., AAPackageManager internals) — stable and with compiler warnings.