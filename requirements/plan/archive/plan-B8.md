# Sub-Plan B8: AssetHandle Struct + HandleRegistry + Error Propagation Unification

> **Risk**: Medium
> **Dependencies**: B7-1 + B7-2 completed; B5-2 AssetHandle contract completed
> **Status**: DONE — signed off 2026-04-07

---

## Objective

Complete runtime handle-model hardening and error propagation unification:

- Convert `AssetHandle<T>` from class to struct (value semantic, lower GC pressure)
- Introduce `HandleRegistry` for centralized handle state lifecycle
- Unify bundle/backend internal error propagation through structured tuples
- Keep external API stable (`IPackageBackend` and `AddressablesBackend` unchanged)

---

## Implemented Scope

1. `AssetHandle<T>` redesigned as struct using `handleId + generation`
2. Added `HandleRegistry` (slot + free list + generation validation)
3. Expanded `AssetLoadError.Code` with:
   - `BundleNotFound`
   - `BundleLoadFailed`
   - `DependencyFailed`
   - `AssetExtractionFailed`
4. `ABBundleLoader` internal load APIs return `(AssetBundle, AssetLoadError)`
5. `ABPackageBackend` internal load APIs return tuple form for manager integration
6. `AssetPackageManager` `LoadByXxx` methods allocate handle states via `HandleRegistry.Alloc`

---

## Out of Scope

- No `CancellationToken` support in this phase (deferred to Phase 9 H1)
- No retry logic in bundle loader layer (deferred to B9 hotfix/download layer)
- No `IPackageBackend` signature change

---

## Sign-off Summary

- Code path verified: all `LoadByXxx` branches construct handles via `HandleRegistry`
- Legacy path preserved: `AddressablesBackend` untouched
- Phase 3 marked complete after B8 sign-off
