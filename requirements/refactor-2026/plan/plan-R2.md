# Sub-Plan R2: Runtime Correctness + Error Contract Unification + Dedup

> **Risk**: Medium (HandleRegistry refcount logic change; ABPackageBackend public API signature change)
> **Dependencies**: R1 (BuildMessage/RuntimeMessage infrastructure)
> **Status**: Realized — HandleRegistry._entryActiveCounts + ABPackageBackend error contract unified + dedup landed
> **Created**: 2026-04-28

---

## Objective

Fix 3 issues discovered in Phase 5 code quality review (2026-04-28):

1. **CR-1**: HandleRegistry-to-AssetCache refcount desynchronization (use-after-free)
2. **MJ-1**: ABPackageBackend dual error contract (exceptions vs RuntimeMessage return)
3. **MJ-3**: Code duplication — PackByDirectory.Fallback ≈ PackByCollectPath.GetPackKey + "default" constant triplicated

MJ-2 (sync/async dedup) excluded — recorded as known debt, over-engineering for 55-line gain.

---

## R2-1: HandleRegistry EntryId-Aware Refcount (CR-1)

### Problem

`AssetCache._assetCache` refcount and `HandleRegistry` per-slot RefCount are independent counters. A direct `UnloadAsset(key)` call can decrement AssetCache refcount, and a subsequent Handle.Release() may trigger `bundle.Unload(true)` while another Handle still references the asset.

### Design

B2 approach: independent slots + EntryId active-count tracking.

```
HandleRegistry new:
  _entryActiveCounts: Dictionary<string, int>

Alloc(entryId, ...):
  _entryActiveCounts[entryId]++
  // ... existing slot allocation ...

Release(handleId, generation):
  // ... existing refcount decrement ...
  if refcount hits 0:
    _entryActiveCounts[entryId]--
    only if _entryActiveCounts[entryId] == 0:
      fire releaseCallback
      remove entry from _entryActiveCounts
```

### Changes

| File | Change |
|------|--------|
| HandleRegistry.cs | Add `_entryActiveCounts` dictionary; wiring in Alloc/Release |
| HandleRegistry.cs | `Reset()` method clears `_entryActiveCounts` |
| ABPackageBackend.cs | Remove `AssetCacheEntry.RefCount` field |
| ABPackageBackend.cs | `AddToAssetCache` — remove RefCount init |
| ABPackageBackend.cs | LoadInternal cache-hit path — remove `cached.RefCount++` |
| ABPackageBackend.cs | `ReleaseEntry` — remove RefCount check, always cleanup when called |
| ABPackageBackend.cs | `UnloadAsset(key)` / `UnloadByEntryId(entryId)` — documented as Handle-only path, unloads when called from Handle callback |

### Invariants
- Each Handle owns one reference; asset unloads only when ALL handles released
- `UnloadAsset(key)` from AssetPackageManager still works (internally finds all handles)
- `_entryActiveCounts` always matches sum of active Handle slot refcounts per entryId

---

## R2-2: ABPackageBackend Error Contract Unified to RuntimeMessage (MJ-1)

### Problem

Public API throws raw `Exception` wrapping `RuntimeMessage`; internal tuple API returns `RuntimeMessage` directly. Callers must know which path they're on.

### Design

Public API returns `RuntimeMessage` instead of throwing. No new exception type needed — this IS the R-series purpose.

```csharp
// BEFORE
public async Task<T> LoadAssetAsync<T>(string key) {
    if (error != null) throw new Exception(error.ToString());
}

// AFTER
public async Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key) {
    if (error != null) return (null, error);
    return (asset, null);
}
```

### Interface Impact

`IPackageBackend` signature changes. This is an interface with two implementations:
- `ABPackageBackend` — changed
- `AddressablesBackend` — changed to match (wrap exceptions as RuntimeMessage)

### Changes

| File | Change |
|------|--------|
| IPackageBackend.cs | Change return types: `Task<T>` → `Task<(T, RuntimeMessage)>` |
| ABPackageBackend.cs | All LoadAssetAsync/Sync methods use RuntimeMessage return |
| AddressablesBackend.cs | Wrap exceptions into RuntimeMessage return |
| AssetPackageManager.cs | Update call sites to destructure (asset, error) tuple |

### Invariants
- All public load methods return `RuntimeMessage` for errors, never throw
- Internal tuple API `LoadAssetTupleAsync/Sync` unchanged (already uses RuntimeMessage)
- Legacy `AddressablesBackend` wraps Addressables exceptions as RuntimeMessage

---

## R2-3: Code Dedup — PackRule + Constants (MJ-3)

### Problem

`PackByDirectory.Fallback()` duplicates `PackByCollectPath.GetPackKey()` logic. "default" fallback constant defined 3 times.

### Design

1. `PackByDirectory.Fallback()` delegates to `PackByCollectPath.GetPackKey()`
2. Move `DefaultPackKey` to `SystemIdentifiers`
3. `BundleNameBuilder.FallbackSegment` references `SystemIdentifiers.DefaultPackKey`

### Changes

| File | Change |
|------|--------|
| PackByDirectory.cs | Remove `Fallback()`, call `PackByCollectPath` static or shared helper |
| PackByCollectPath.cs | `FallbackPackKey` → reference `SystemIdentifiers.DefaultPackKey` |
| BundleNameBuilder.cs | `FallbackSegment` → reference `SystemIdentifiers.DefaultPackKey` |
| SystemIdentifiers.cs | Add `DefaultPackKey = "default"` |

### Invariants
- All PackRule output identical to pre-dedup
- Build verification passes

---

## Task Breakdown

| Task | Content | Files | Risk |
|------|---------|-------|------|
| R2-T1 | HandleRegistry: add `_entryActiveCounts` + wire Alloc/Release/Reset | 1 | Medium |
| R2-T2 | ABPackageBackend: remove AssetCache.RefCount + update cache paths | 1 | Medium |
| R2-T3 | R2-1 build verification | — | — |
| R2-T4 | IPackageBackend: change signatures to RuntimeMessage return | 1 | Medium |
| R2-T5 | ABPackageBackend: update public load methods | 1 | Low |
| R2-T6 | AddressablesBackend: wrap exceptions as RuntimeMessage | 1 | Low |
| R2-T7 | AssetPackageManager: update call sites | 1 | Low |
| R2-T8 | R2-2 build verification | — | — |
| R2-T9 | PackByDirectory: delegate fallback to PackByCollectPath | 2 | Low |
| R2-T10 | SystemIdentifiers: add DefaultPackKey + update 3 refs | 4 | Low |
| R2-T11 | R2-3 build verification | — | — |

---

## New Files

None — all changes are modifications to existing files.

---

## Modified Files

| File | R2-1 | R2-2 | R2-3 |
|------|------|------|------|
| HandleRegistry.cs | ✓ | | |
| IPackageBackend.cs | | ✓ | |
| ABPackageBackend.cs | ✓ | ✓ | |
| AddressablesBackend.cs | | ✓ | |
| AssetPackageManager.cs | | ✓ | |
| PackByDirectory.cs | | | ✓ |
| PackByCollectPath.cs | | | ✓ |
| BundleNameBuilder.cs | | | ✓ |
| SystemIdentifiers.cs | | | ✓ |

---

## Invariants (Must Hold After R2)

1. Same asset loaded via 2+ Handles: asset unloads only when ALL handles released
2. Direct `UnloadAsset(key)` correctly releases all handles for that key
3. All public load methods return `(T, RuntimeMessage)`, never throw on expected errors
4. Sync/async behavior identical to pre-dedup
5. PackRule output identical to pre-dedup
6. `dotnet build XLuaHotfix.sln` passes with 0 errors

---

## Approval Checklist

- [x] Agree to B2 approach: independent slots + `_entryActiveCounts` per-EntryId tracking
- [x] Agree to IPackageBackend signature change: `Task<T>` → `Task<(T, RuntimeMessage)>`
- [x] Agree to sync/async dedup strategy: RECORDED AS KNOWN DEBT, excluded from R2 (over-engineering for 55-line gain)
- [x] Agree to PackByDirectory delegating to PackByCollectPath for fallback
- [x] Agree to `DefaultPackKey` centralized in SystemIdentifiers
- [x] Agree to 11 tasks (MJ-2 removed), 0 new files, 9 modified files

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-28 | Initial version: CR-1 + MJ-1 + MJ-2 + MJ-3 |
