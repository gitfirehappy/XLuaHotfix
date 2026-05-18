# Sub-Plan B7-1: ABBundleLoader — Bundle File Loading & Dependency Resolution

> **Risk**: Medium
> **Dependencies**: B6 (ABManifest data layer with BundleEntries + dependency indices)
> **Status**: DONE — signed off 2026-04-07

---

## Objective

Rewrite `ABBundleLoader` from a 2-method stub into a complete Bundle file loading/unloading engine:

- Load AssetBundle files from disk (sync + async)
- Recursively load dependency bundles before the target bundle
- Maintain a bundle-level cache with reference counting
- Unload bundles when their reference count reaches 0

This replaces the Addressables internal BundleProvider that previously handled bundle loading invisibly.

### Addressables Counterpart (what we're replacing)

```
Addressables internal:
  Catalog → Locator → BundleProvider
    → AssetBundleResource.BeginOperation()
    → AssetBundle.LoadFromFileAsync(path)
    → resolve dependencies from catalog → load dependency bundles
    → reference tracked internally by Addressables ResourceManager
```

ABBundleLoader replaces all of the above with explicit, user-controllable logic.

---

## Confirmed Design Decisions

### A. Bundle Path Resolution

Same strategy as ManifestLoader (B6):
1. Primary: `Path.Combine(PathManager.CurrentGUIDRoot, bundleName)`
2. Fallback: `Path.Combine(Application.streamingAssetsPath, bundleName)`
3. Use `File.Exists()` to check primary first
4. Android StreamingAssets special handling deferred (noted, not implemented in B7-1)

### B. Dependency Resolution Strategy

- Use `ABManifest.GetDirectDependencies(bundleEntry)` to get direct dependencies
- Recursively resolve all transitive dependencies (depth-first)
- Use a `HashSet<string>` (bundleName) to prevent cycles and duplicates
- Load all dependency bundles BEFORE loading the target bundle
- Each dependency bundle gets its own reference count increment

### C. Bundle-Level Cache

```csharp
// Internal structure
private class BundleCacheEntry
{
    public AssetBundle Bundle;
    public int RefCount;
    public string[] DependencyBundleNames; // for decrement on unload
}

private Dictionary<string, BundleCacheEntry> _bundleCache;
```

- Cache keyed by `BundleName` (from ManifestBundleEntry)
- RefCount starts at 1 on first load
- Each subsequent load of the same bundle increments RefCount
- Each dependency bundle also tracked with its own RefCount

### D. Reference Count Rules

- **LoadBundle(bundleName)**: If cached, RefCount++. If not cached, load from file, add to cache with RefCount=1
- **LoadBundle with dependencies**: Loading bundle X triggers loading deps [A, B, C]. Each dep gets RefCount++ independently
- **UnloadBundle(bundleName)**: RefCount--. If RefCount reaches 0:
  - `AssetBundle.Unload(true)`
  - Decrement RefCount of all dependency bundles (recursive)
  - Remove from cache

### E. Sync vs Async

Both paths implemented:
- **Async**: `AssetBundle.LoadFromFileAsync(path)` → `await bundleRequest`
- **Sync**: `AssetBundle.LoadFromFile(path)` (direct, blocks main thread)
- Dependency loading follows the same sync/async pattern as the caller

### F. Error Handling

- Bundle file not found → `Debug.LogError` + return null
- Bundle load fails → `Debug.LogError` + return null
- Dependency load fails → stop loading, log which dep failed, return null for the whole chain
- Do not throw exceptions — return null on failure (consistent with Unity conventions for asset loading)

---

## Planned Implementation

### Class: ABBundleLoader

Location: `Assets/AboutXLua/Scripts/Core/Hotfix_AssetPackageManage/Runtime/Backends/AB/ABBundleLoader.cs`

```
ABBundleLoader (non-static, instance class)
├── Constructor(ABManifest manifest)
├── Async API
│   ├── Task<AssetBundle> LoadBundleAsync(string bundleName)
│   └── Task UnloadBundleAsync(string bundleName)  // if needed
├── Sync API
│   ├── AssetBundle LoadBundle(string bundleName)
│   └── void UnloadBundle(string bundleName)
├── Query
│   ├── bool IsBundleLoaded(string bundleName)
│   └── int GetBundleRefCount(string bundleName)
├── Lifecycle
│   └── void UnloadAllBundles()
└── Internal
    ├── BundleCacheEntry (nested class)
    ├── Dictionary<string, BundleCacheEntry> _bundleCache
    ├── ABManifest _manifest
    ├── ResolveBundlePath(string bundleName) → string
    ├── LoadDependenciesAsync(ManifestBundleEntry, HashSet<string> visited)
    └── LoadDependenciesSync(ManifestBundleEntry, HashSet<string> visited)
```

### Key Methods

#### LoadBundleAsync(string bundleName)

```
1. Check _bundleCache for bundleName
   → HIT: entry.RefCount++, return entry.Bundle
2. Resolve physical path: ResolveBundlePath(bundleName)
   → null: log error, return null
3. Get ManifestBundleEntry from ABManifest via TryGetBundleByName
4. Recursively load all dependencies first (LoadDependenciesAsync)
   → Any dep fails: log error, return null
5. AssetBundle.LoadFromFileAsync(path) → await
   → Fails: log error, unload already-loaded deps for this call, return null
6. Create BundleCacheEntry { Bundle, RefCount=1, DependencyBundleNames }
7. Add to _bundleCache
8. Return AssetBundle
```

#### UnloadBundle(string bundleName)

```
1. Check _bundleCache for bundleName
   → MISS: return (no-op, already unloaded)
2. entry.RefCount--
3. If RefCount <= 0:
   a. entry.Bundle.Unload(true)
   b. For each dependency in entry.DependencyBundleNames:
      → UnloadBundle(depName) (recursive decrement)
   c. _bundleCache.Remove(bundleName)
```

#### ResolveBundlePath(string bundleName)

```
1. string primary = Path.Combine(PathManager.CurrentGUIDRoot, bundleName)
   → File.Exists(primary) → return primary
2. string fallback = Path.Combine(Application.streamingAssetsPath, bundleName)
   → File.Exists(fallback) → return fallback
3. return null (not found)
```

---

## Preservation Requirements (Must Pass)

- [ ] ABBundleLoader does NOT depend on Addressables — zero Addressables imports
- [ ] ABBundleLoader does NOT directly depend on ABAssetIndex — only uses ABManifest for bundle queries
- [ ] Bundle reference counting is correct: every LoadBundle has a corresponding UnloadBundle
- [ ] Dependency cycles are handled (HashSet prevents infinite recursion)
- [ ] Sync and async paths are independent — sync does not call async internally
- [ ] ABBundleLoader is an instance class (not static) — can be created/disposed per session

---

## Acceptance Criteria

- [ ] ABBundleLoader replaces the 2-method stub with full implementation
- [ ] Async bundle loading works: LoadBundleAsync returns a valid AssetBundle
- [ ] Sync bundle loading works: LoadBundle returns a valid AssetBundle
- [ ] Dependencies are loaded before the target bundle
- [ ] Bundle reference counting correctly tracks load/unload pairs
- [ ] UnloadBundle(true) is called only when refcount reaches 0
- [ ] Dependency bundles are recursively unloaded when owning bundle is fully released
- [ ] No Addressables dependency in any import or call
- [ ] Compilation passes (same verification level as B6)

---

## Out of Scope

- Asset-level loading from bundle (bundle.LoadAsset) → B7-2
- Asset-level caching and refcounting → B7-2
- AssetHandle pooling → B8
- Android StreamingAssets via UnityWebRequest → deferred
- Bundle encryption/decryption → deferred (ManifestBundleEntry.Encrypted field reserved)
- CRC validation on load → deferred (ManifestBundleEntry.FileCRC field reserved)
