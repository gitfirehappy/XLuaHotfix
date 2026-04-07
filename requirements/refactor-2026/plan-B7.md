# Sub-Plan B7: ABPackageBackend — Custom AB Runtime Loading Backend

> **Status**: Approved — 2026-04-07, all 8 design decisions confirmed
> **Dependencies**: B6 (ABAssetIndex + ManifestLoader) completed; B5-2 (IPackageBackend contract) completed
> **Scope**: Runtime AB loading backend only — replaces AddressablesBackend with direct AssetBundle loading
> **Sub-files**: plan-B7-1.md / plan-B7-2.md

---

## Background & Objectives

B6 replaced the **index source** (AddressableLabelsConfig → ABAssetIndex backed by ABManifest).
However, the actual asset loading still goes through `AddressablesBackend` → `Addressables.LoadAssetAsync()`.

B7 is the next step: replace the **loading backend** so that assets are loaded directly from AssetBundles
without depending on Unity Addressables at runtime.

### Old Architecture (Addressables)

```
User → AAPackageManager.LoadAssetAsync<T>(key)
  → AddressablesBackend._resourceCache check
    → HIT:  refcount++, return cached
    → MISS: Addressables.LoadAssetAsync<T>(key)
      → (Addressables internal: Catalog → Locator → BundleProvider → AssetBundle)
      → AddToCache(key, handle)
      → return asset
```

### New Architecture (B7 target)

```
User → AAPackageManager.LoadAssetAsync<T>(key)
  → ABPackageBackend._assetCache check
    → HIT:  refcount++, return cached
    → MISS: Query ABManifest: entry → BundleEntry → BundleName
      → ABBundleLoader.LoadBundle(bundleName) [with dependency resolution]
        → _bundleCache check
          → HIT:  bundle refcount++
          → MISS: AssetBundle.LoadFromFile(path) + load all dependency bundles
        → bundle.LoadAsset<T>(entry.SourcePath)
      → AddToAssetCache(cacheKey, asset, bundleName)
      → return asset
```

### What B7 Replaces (Addressables → Custom AB)

| Addressables Component | Custom AB Replacement | Sub-plan |
|------------------------|----------------------|----------|
| Addressables.LoadAssetAsync / Release | ABPackageBackend.LoadAssetAsync / UnloadAsset | B7-2 |
| Internal BundleProvider + dependency loading | ABBundleLoader (Bundle file I/O + recursive deps) | B7-1 |
| Internal handle cache + refcount | Dual-layer cache: Bundle-level + Asset-level refcount | B7-1 + B7-2 |
| Catalog → Locator (address → bundle mapping) | ABManifest (Asset→BundleIndex→BundleEntry) | B6 ✅ (already done) |
| InternalIdTransformFunc (path rewriting) | PathManager.CurrentGUIDRoot + BundleName | B7-1 |

---

## In Scope

- ABBundleLoader: AssetBundle file loading/unloading with dependency resolution and bundle-level caching
- ABPackageBackend: Full IPackageBackend implementation with asset-level caching, delegating to ABBundleLoader
- Integration with AAPackageManager via USE_AB_INDEX const switch (index + backend switch together)
- Both sync and async loading paths
- Dual-layer reference counting (Bundle-level in B7-1, Asset-level in B7-2)

## Out of Scope

- AssetHandle pooling / lifecycle management → B8
- CatalogUpdater replacement / HotfixManager adaptation → B4/B9
- Build-time ABManifest generation → Phase 5-6
- Multi-platform StreamingAssets special handling (Android UnityWebRequest) → deferred
- LRU/LFU eviction strategies → Phase 9
- AsyncOp priority scheduling → Phase 9

---

## Sub-Plan Index

| File | Content | Dependency |
|------|---------|------------|
| plan-B7-1.md | B7-1: ABBundleLoader — Bundle file I/O + dependency resolution + bundle cache | B6 (ABManifest) |
| plan-B7-2.md | B7-2: ABPackageBackend — IPackageBackend impl + asset cache + AAPackageManager integration | B7-1 |

### Execution Order

```
B7-1 (ABBundleLoader) → B7-2 (ABPackageBackend + integration)
```

B7-1 must complete first because B7-2 delegates all Bundle operations to ABBundleLoader.

---

## Key Design Decisions

### D1. Asset → Bundle Resolution Chain

ABPackageBackend resolves an address/entryId to a bundle via ABManifest:
1. `ABAssetIndex.GetEntriesByAddress(address)` → `ManifestAssetEntry`
2. `ABManifest.GetBundleForAsset(entry)` → `ManifestBundleEntry`
3. `ManifestBundleEntry.BundleName` → physical bundle filename

ABPackageBackend holds a reference to ABManifest (passed in from AAPackageManager, reusing the B6-loaded instance).

### D2. Bundle Path Strategy

Same as ManifestLoader (B6):
- Primary: `Path.Combine(PathManager.CurrentGUIDRoot, bundleName)`
- Fallback: `Path.Combine(Application.streamingAssetsPath, bundleName)`

### D3. Asset Internal Name (bundle.LoadAsset key)

First round uses `ManifestAssetEntry.SourcePath` (e.g., `"Assets/Prefabs/Player.prefab"`).
This is the standard Unity AssetBundle behavior — assets are stored with their project-relative path.
Future build pipeline (Phase 5-6) may adjust this; the field is already in ManifestAssetEntry.

### D4. Dual-Layer Reference Counting

- **Bundle level** (ABBundleLoader): BundleName → {AssetBundle, RefCount}
  - RefCount tracks how many assets currently loaded from this bundle
  - When RefCount reaches 0 → `AssetBundle.Unload(true)` and remove from cache
  - Dependency bundles also tracked with their own refcount
- **Asset level** (ABPackageBackend): CacheKey → {Object, BundleName, RefCount}
  - CacheKey = address string (consistent with AddressablesBackend legacy behavior)
  - When asset RefCount reaches 0 → remove from asset cache → decrement owning bundle's RefCount
  - EntryId-based unload maps through ABManifest to find the corresponding address/bundle

### D5. AssetBundle.Unload(true) vs Unload(false)

Use `Unload(true)` — unloads the bundle and all loaded assets from it.
This is safe because: when bundle RefCount=0, it means all assets from this bundle have been released.
Reference counting correctness guarantees safety.

### D6. Integration Switch

Expand existing `USE_AB_INDEX` const in AAPackageManager:
- When `USE_AB_INDEX == true`: use ABAssetIndex (B6) **AND** ABPackageBackend (B7)
- When `USE_AB_INDEX == false`: use AddressableLabelsConfig + AddressablesBackend (legacy)

One switch controls both dimensions — there is no valid "AB index + Addressables backend" combination.

### D7. ABPackageBackend Dependencies

- `ABManifest` — for Asset→Bundle resolution (passed from AAPackageManager)
- `ABBundleLoader` — for Bundle file loading/unloading (created internally)
- `PathManager` — for bundle file path resolution (static access)
- Does NOT directly depend on ABAssetIndex (uses ABManifest for bundle queries)

---

## Verification Strategy

Same as B6: **compilation-level verification** in this round.
- No build pipeline generates ABManifest.json with real bundle data yet
- End-to-end runtime verification deferred to after Phase 5-6 (build export tools)
- B7 guarantees: compilation passes + full IPackageBackend contract implemented + const switch integration

---

## Approval Checklist

- [x] Should B7 be split into B7-1 (ABBundleLoader) + B7-2 (ABPackageBackend)?
  **Decision**: Yes. B7-1 handles bundle I/O + deps + cache; B7-2 handles IPackageBackend impl + asset cache + integration.
- [x] Should bundle path strategy match ManifestLoader (CurrentGUIDRoot primary, StreamingAssets fallback)?
  **Decision**: Yes. Consistent with B6 ManifestLoader path strategy.
- [x] Should bundle.LoadAsset use ManifestAssetEntry.SourcePath as internal asset name?
  **Decision**: Yes. First round uses SourcePath; adjustable when build pipeline (Phase 5-6) is implemented.
- [x] Should ABPackageBackend use dual-layer caching (Bundle-level + Asset-level refcount)?
  **Decision**: Yes. Bundle-level in ABBundleLoader, Asset-level in ABPackageBackend.
- [x] Should AssetBundle.Unload(true) be used when bundle refcount reaches 0?
  **Decision**: Yes. Refcount correctness guarantees safety.
- [x] Should USE_AB_INDEX const control both index and backend switching?
  **Decision**: Yes. One switch, two dimensions. No valid "AB index + Addressables backend" combination.
- [x] Should CatalogUpdater replacement be explicitly excluded from B7 (deferred to B4)?
  **Decision**: Yes.
- [x] Should AssetHandle pooling be explicitly excluded from B7 (deferred to B8)?
  **Decision**: Yes.
