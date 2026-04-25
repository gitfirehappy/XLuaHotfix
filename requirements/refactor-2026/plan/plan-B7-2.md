# Sub-Plan B7-2: ABPackageBackend — IPackageBackend Implementation & AssetPackageManager Integration

> **Risk**: Medium
> **Dependencies**: B7-1 (ABBundleLoader) + B6 (ABAssetIndex + ABManifest)
> **Status**: DONE — signed off 2026-04-07

---

## Objective

Implement `ABPackageBackend` as a full `IPackageBackend` replacement for `AddressablesBackend`:

- Resolve address/entryId → ManifestAssetEntry → Bundle → Asset
- Delegate Bundle file operations to ABBundleLoader (B7-1)
- Extract assets from loaded bundles via `AssetBundle.LoadAsset<T>()`
- Maintain asset-level cache with reference counting
- Integrate with AssetPackageManager via the existing USE_AB_INDEX const switch

This is the **direct equivalent** of `AddressablesBackend` — same external behavior, zero Addressables dependency.

### Addressables Counterpart (what we're replacing)

```
AddressablesBackend:
  _resourceCache: Dictionary<string, ResourceEntry>  // key → {Handle, RefCount}
  LoadAssetAsync<T>(key):
    → cache HIT: refcount++, return cached
    → cache MISS: Addressables.LoadAssetAsync<T>(key) → cache → return
  UnloadAsset(key):
    → refcount-- → 0: Addressables.Release(handle), remove cache

ABPackageBackend (replacement):
  _assetCache: Dictionary<string, AssetCacheEntry>  // key → {Object, BundleName, RefCount}
  LoadAssetAsync<T>(key):
    → cache HIT: refcount++, return cached
    → cache MISS: ABManifest resolve → ABBundleLoader.LoadBundle → bundle.LoadAsset → cache → return
  UnloadAsset(key):
    → refcount-- → 0: remove from asset cache → ABBundleLoader.UnloadBundle(bundleName)
```

---

## Confirmed Design Decisions

### A. Asset Resolution Chain

For a given key (address string):
1. Query ABManifest: `TryGetAssetsByAddress(key)` → `List<ManifestAssetEntry>`
2. Select the first match (V1 strategy: first-match; type disambiguation handled by caller via LoadByAddress<T>)
3. Get bundle: `ABManifest.GetBundleForAsset(entry)` → `ManifestBundleEntry`
4. Load bundle: `ABBundleLoader.LoadBundle(entry.BundleName)` → `AssetBundle`
5. Extract asset: `bundle.LoadAsset<T>(entry.SourcePath)` → `T`

For entryId-based loading (B5-2 overloads):
1. Query ABManifest: `TryGetAssetByEntryId(entryId)` → `ManifestAssetEntry`
2. Same steps 3-5 as above

### B. Asset-Level Cache

```csharp
private class AssetCacheEntry
{
    public UnityEngine.Object Asset;
    public string BundleName;    // which bundle this asset came from
    public string EntryId;       // for entryId-based unload
    public int RefCount;
}

// Primary cache: keyed by address (legacy API compatibility)
private Dictionary<string, AssetCacheEntry> _assetCache;

// Secondary lookup: entryId → address (for UnloadByEntryId)
private Dictionary<string, string> _entryIdToAddress;
```

### C. Reference Count Flow

```
LoadAssetAsync("PlayerPrefab"):
  → _assetCache["PlayerPrefab"] exists?
    → YES: refcount++, return cached asset
    → NO:
      1. ABManifest resolve → BundleName = "prefabs_abc.bundle"
      2. ABBundleLoader.LoadBundleAsync("prefabs_abc.bundle")
         → (bundle cache: refcount++ for bundle + deps)
      3. bundle.LoadAssetAsync<T>("Assets/Prefabs/Player.prefab")
      4. _assetCache["PlayerPrefab"] = {asset, "prefabs_abc.bundle", entryId, refcount=1}
      5. Return asset

UnloadAsset("PlayerPrefab"):
  → _assetCache["PlayerPrefab"] exists?
    → NO: return (no-op)
    → YES:
      1. entry.RefCount--
      2. If RefCount <= 0:
         a. Remove from _assetCache
         b. Remove from _entryIdToAddress
         c. ABBundleLoader.UnloadBundle("prefabs_abc.bundle")
            → (bundle refcount--, unloads AB if 0)

UnloadByEntryId("guid-12345"):
  → _entryIdToAddress["guid-12345"] → address = "PlayerPrefab"
  → UnloadAsset("PlayerPrefab")
```

### D. Async Asset Extraction

For `bundle.LoadAsset<T>()`:
- **Async**: `AssetBundleRequest request = bundle.LoadAssetAsync<T>(name)` → `await` via `Task` wrapper
- **Sync**: `T asset = bundle.LoadAsset<T>(name)` (direct call)

Note: `AssetBundleRequest` is not `Task`-based; needs a utility wrapper (`AssetBundleRequest` → `Task<T>`).

### E. Error Handling

- Address not found in ABManifest → return null (legacy path) or throw (B5-2 path uses AssetHandle error)
- Bundle load fails → return null + log error
- Asset extraction fails (null result from bundle.LoadAsset) → return null + log error
- Consistent with AddressablesBackend: async throws on failure, sync returns null

### F. Integration with AssetPackageManager

Expand the `USE_AB_INDEX` switch in `AssetPackageManager.Initialize()`:

```
When USE_AB_INDEX == true:
  1. ManifestLoader.LoadAsync() → ABManifest        (B6, already done)
  2. new ABAssetIndex(manifest) → set as _index      (B6, already done)
  3. new ABBundleLoader(manifest) → held internally   (B7-1, NEW)
  4. new ABPackageBackend(manifest, bundleLoader)     (B7-2, NEW)
     → SetBackend(abBackend)                          (replace AddressablesBackend)
  5. Build _labelToKeys from _index                   (B6, already done)
```

---

## Planned Implementation

### Class: ABPackageBackend

Location: `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/Runtime/Backends/AB/ABPackageBackend.cs`

```
ABPackageBackend : IPackageBackend
├── Constructor(ABManifest manifest, ABBundleLoader bundleLoader)
├── IPackageBackend.InitializeAsync() → Task (no-op, init done in constructor)
├── IPackageBackend.LoadAssetAsync<T>(string key)
├── IPackageBackend.LoadAssetSync<T>(string key)
├── IPackageBackend.UnloadAsset(string key)
├── IPackageBackend.ContainsKey(string key)
├── IPackageBackend.LoadAssetAsync<T>(string key, string entryId)  — B5-2 overload
├── IPackageBackend.LoadAssetSync<T>(string key, string entryId)   — B5-2 overload
├── IPackageBackend.UnloadByEntryId(string entryId)                — B5-2 overload
└── Internal
    ├── AssetCacheEntry (nested class)
    ├── Dictionary<string, AssetCacheEntry> _assetCache
    ├── Dictionary<string, string> _entryIdToAddress
    ├── ABManifest _manifest
    ├── ABBundleLoader _bundleLoader
    ├── ResolveAssetEntry(string address) → ManifestAssetEntry
    └── AssetBundleRequestToTask<T>(AssetBundleRequest) → Task<T>
```

### Key Methods

#### LoadAssetAsync\<T\>(string key)

```
1. Check _assetCache for key
   → HIT: entry.RefCount++, return entry.Asset as T
2. Resolve: _manifest.TryGetAssetsByAddress(key, out entries)
   → MISS: throw or return null (see E. Error Handling)
   → entries[0] (first match, V1 strategy)
3. Get bundle entry: _manifest.GetBundleForAsset(assetEntry)
4. Load bundle: await _bundleLoader.LoadBundleAsync(bundleEntry.BundleName)
   → null: throw "[ABPackageBackend] Bundle load failed: {bundleName}"
5. Extract asset: await AssetBundleRequestToTask<T>(bundle.LoadAssetAsync<T>(assetEntry.SourcePath))
   → null: log error, _bundleLoader.UnloadBundle(bundleName), throw
6. Create AssetCacheEntry { Asset=asset, BundleName, EntryId=assetEntry.EntryId, RefCount=1 }
7. _assetCache[key] = entry
8. _entryIdToAddress[assetEntry.EntryId] = key
9. Return asset
```

#### LoadAssetAsync\<T\>(string key, string entryId) — B5-2 overload

```
1. Check _assetCache for key
   → HIT: entry.RefCount++, return entry.Asset as T
2. Resolve by entryId: _manifest.TryGetAssetByEntryId(entryId, out assetEntry)
   → MISS: fallback to address-based resolve (same as basic overload)
3. Same steps 3-9 as above, using resolved assetEntry
```

#### UnloadAsset(string key) / UnloadByEntryId(string entryId)

```
UnloadByEntryId:
  → _entryIdToAddress[entryId] → key → UnloadAsset(key)

UnloadAsset:
  1. _assetCache[key] → entry
     → MISS: return (no-op)
  2. entry.RefCount--
  3. If RefCount <= 0:
     a. _assetCache.Remove(key)
     b. _entryIdToAddress.Remove(entry.EntryId)
     c. _bundleLoader.UnloadBundle(entry.BundleName)
```

### Utility: AssetBundleRequest → Task\<T\> Wrapper

```csharp
private static Task<T> AssetBundleRequestToTask<T>(AssetBundleRequest request) where T : UnityEngine.Object
{
    var tcs = new TaskCompletionSource<T>();
    request.completed += _ =>
    {
        tcs.SetResult(request.asset as T);
    };
    return tcs.Task;
}
```

---

## Preservation Requirements (Must Pass)

- [ ] ABPackageBackend does NOT import any Addressables namespace
- [ ] All IPackageBackend methods are implemented (including B5-2 default overloads)
- [ ] External behavior matches AddressablesBackend: same cache-hit returns, same refcount semantics
- [ ] AssetPackageManager integration uses the same USE_AB_INDEX const (no new switches)
- [ ] Legacy code path (USE_AB_INDEX == false) remains completely unchanged
- [ ] ABPackageBackend does not directly call AssetBundle.LoadFromFile — delegates to ABBundleLoader

---

## Acceptance Criteria

- [ ] ABPackageBackend replaces the stub with full IPackageBackend implementation
- [ ] LoadAssetAsync<T>(key) resolves through ABManifest → ABBundleLoader → bundle.LoadAsset
- [ ] LoadAssetSync<T>(key) works synchronously through the same chain
- [ ] Asset-level refcount cache works correctly (load increments, unload decrements)
- [ ] UnloadByEntryId correctly maps to address-based unload
- [ ] ContainsKey checks asset cache presence
- [ ] AssetPackageManager.Initialize() creates ABBundleLoader + ABPackageBackend when USE_AB_INDEX == true
- [ ] No Addressables dependency in any import or call
- [ ] Compilation passes

---

## Out of Scope

- ABBundleLoader implementation → B7-1 (prerequisite)
- AssetHandle pooling / object reuse → B8
- CatalogUpdater / HotfixManager adaptation → B4
- Build-time ABManifest generation → Phase 5-6
- Type disambiguation on address collision → handled by AssetResolver (B5-2), not backend
- CRC validation on bundle load → deferred
- Bundle encryption/decryption → deferred
