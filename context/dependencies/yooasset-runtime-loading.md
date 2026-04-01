# YooAsset Runtime Loading Reference

> Source: YooAsset source code analysis (Runtime/ResourceManager/, Runtime/ResourcePackage/)
> Purpose: Comparison reference for XLuaHotfix B5-2 AssetHandle / AssetResolver / AAPackageManager design
> Note: Our runtime design may be refactored; use this as architectural inspiration, not as a strict template
> Language: English (AI consumption)

---

## 1. Architecture Overview

YooAsset's runtime loading system is a layered architecture:

```
User Code
  -> ResourcePackage (facade per package)
     -> ResourceManager (loading + caching)
        -> ProviderOperation (per-asset async task)
           -> LoadBundleFileOperation (per-bundle loader)
              -> IFileSystem (storage abstraction)
     -> PlayModeImpl (mode routing + IBundleQuery)
        -> PackageManifest (asset-to-bundle mapping)
```

### Key Design Principles:
- **Handle-first**: Users hold lightweight handles, not raw assets
- **Reference counting**: At both Provider and Bundle level
- **Provider caching**: Same asset GUID reuses existing Provider
- **Async-first**: All operations are AsyncOperationBase subclasses
- **Mode-agnostic**: Same API across Editor/Offline/Host/Web modes

---

## 2. Loading Flow (Complete)

For `package.LoadAssetAsync<Sprite>("UI/icon")`:

```
1. ResourcePackage.LoadAssetAsync(location, type)
   |
2. ManifestTools.ConvertLocationToAssetInfo(location, type)
   -> PackageManifest lookup: Location -> AssetPath -> PackageAsset
   -> Build AssetInfo (contains GUID, BundleID, DependBundleIDs)
   |
3. ResourceManager.LoadAssetAsync(assetInfo)
   -> Check ProviderDic for existing provider (key: "LoadAssetAsync" + GUID)
   -> If exists: reuse, RefCount++
   -> If not: create new AssetProvider, add to ProviderDic
   -> OperationSystem.StartOperation(provider)
   |
4. AssetProvider.InternalOnUpdate() [driven by OperationSystem each frame]
   -> ESteps.StartBundleLoader:
        IBundleQuery.GetMainBundleInfo(assetInfo) -> BundleInfo
        IBundleQuery.GetDependBundleInfos(assetInfo) -> List<BundleInfo>
        Create/reuse LoadBundleFileOperation for each (LoaderDic caching)
   -> ESteps.WaitBundleLoader:
        Wait for all bundle loaders to complete
   -> ESteps.ProcessBundleResult:
        mainBundle.Result.AssetBundle.LoadAssetAsync(assetPath, type)
        Wait for Unity async request
   -> ESteps.Done:
        Set AssetObject, mark Succeed
   |
5. LoadBundleFileOperation
   -> IFileSystem.Belong(bundle) to determine file system
   -> IFileSystem.NeedDownload(bundle)?
        Yes -> IFileSystem.DownloadFileAsync() -> wait
   -> IFileSystem.LoadBundleFile(bundle)
        -> AssetBundle.LoadFromFileAsync() or decrypted load
   -> Return BundleResult (holds AssetBundle reference)
   |
6. Handle completion
   -> AssetHandle.AssetObject = provider.AssetObject
   -> Trigger Completed event / Task completion / coroutine resume
```

---

## 3. Provider System

### 3.1 Provider Types

| Provider | Purpose | Asset Output |
|----------|---------|-------------|
| AssetProvider | Load single asset | AssetObject (Unity.Object) |
| SubAssetsProvider | Load all sub-assets | SubAssetObjects[] |
| AllAssetsProvider | Load all assets in bundle | AllAssetObjects[] |
| SceneProvider | Load scene | SceneObject |
| RawFileProvider | Load raw file | File data (bytes/text) |
| CompletedProvider | Immediate success/failure | Error handling shell |

### 3.2 Provider State Machine (ESteps)

```
None -> StartBundleLoader -> WaitBundleLoader -> ProcessBundleResult -> Done
```

Each concrete Provider implements ProcessBundleResult differently (e.g., AssetProvider calls LoadAssetAsync, SceneProvider calls LoadSceneAsync).

### 3.3 Provider Caching

```
ProviderDic: Dictionary<string, ProviderOperation>
  Key = operationType + "_" + assetGUID
  e.g., "LoadAssetAsync_abc123def456"
```

Same key = reuse existing provider, increment RefCount. This prevents duplicate loads of the same asset.

---

## 4. Handle System

### 4.1 Handle Hierarchy

```
HandleBase (abstract, IEnumerator, IDisposable)
  +-- AssetHandle        (AssetObject property)
  +-- SubAssetsHandle    (SubAssetObjects[] property)
  +-- AllAssetsHandle    (AllAssetObjects[] property)
  +-- SceneHandle        (SceneObject property)
  +-- RawFileHandle      (file content properties)
```

### 4.2 Usage Patterns

```csharp
// Coroutine
AssetHandle handle = package.LoadAssetAsync<Sprite>("icon");
yield return handle;
var sprite = handle.AssetObject as Sprite;
handle.Release();

// async/await
await handle.Task;

// Event callback
handle.Completed += (h) => { /* use h.AssetObject */ };

// IDisposable (using statement)
using var handle = package.LoadAssetAsync<Sprite>("icon");
```

### 4.3 Reference Counting

```
Provider Level:
  CreateHandle() -> RefCount++
  ReleaseHandle() -> RefCount--
  RefCount == 0 && not loading -> eligible for destruction

Bundle Level:
  Provider created -> Reference() on main + depend bundle loaders
  Provider destroyed -> Release() on all bundle loaders
  All providers released -> bundle can be unloaded

Auto-Unload:
  If AutoUnloadBundleWhenUnused=true:
    RefCount reaches 0 -> TryUnloadBundle() -> destroy loader

Weak References (optional):
  UseWeakReferenceHandle=true -> WeakReference<HandleBase>
  GC collects abandoned handles -> auto-decrement RefCount
```

**Critical**: Users MUST call Release() or Dispose(). Framework does NOT auto-reclaim handles (unless weak references enabled).

---

## 5. Concurrency Control

```csharp
// Bundle loading concurrency
parameters.BundleLoadingMaxConcurrency = 10;  // max simultaneous bundle loads
```

When exceeded, `LockLoadOperation = true` on providers, causing them to wait in WaitBundleLoader state until a slot opens.

---

## 6. OperationSystem (Async Scheduler)

All async operations inherit from AsyncOperationBase:

```
Features:
  - IEnumerator: yield return support
  - IComparable: priority-based ordering
  - GetAwaiter(): async/await support
  - Completed event: callback on finish
  - WaitForAsyncComplete(): force synchronous (loops Update until done)
  - Child operations: parent tracks all children for progress

Time Slicing:
  OperationSystem.MaxTimeSlice = 30; // ms per frame
  If frame budget exceeded, remaining operations deferred to next frame
  Prevents loading from blocking game loop
```

---

## 7. Comparison with XLuaHotfix Design

### Mapping Table

| YooAsset Concept | XLuaHotfix Equivalent | Status |
|-----------------|----------------------|--------|
| ResourcePackage | AAPackageManager | Existing |
| PackageManifest | ABManifest | Implemented (B6-manifest) |
| PackageAsset | RuntimeAssetEntry / ManifestAssetEntry | Implemented (B5-1) |
| PackageBundle | ManifestBundleEntry | Implemented (B6-manifest) |
| AssetHandle | AssetHandle<T> | Implemented (B5-2) |
| ProviderOperation | (not yet) | Future: B7/B8 |
| LoadBundleFileOperation | ABBundleLoader (stub) | Future: B7 |
| IFileSystem | (not yet) | Future: consider for B7 |
| ResourceManager.ProviderDic | AAPackageManager._pool | Existing (ref-count pool) |
| PlayModeImpl / IBundleQuery | IPackageBackend | Implemented (B2) |
| OperationSystem | (not yet) | Future: Phase 9 (H1) |
| ManifestTools | AssetResolver | Implemented (B5-2) |
| EPlayMode | (not direct) | AddressablesBackend / ABPackageBackend switch |

### Key Architectural Differences

1. **Our AssetHandle is generic**: `AssetHandle<T>` carries type info and Result-style error; YooAsset's AssetHandle uses runtime casting
2. **Our resolve is separate from load**: AssetResolver resolves entries, then AAPackageManager loads; YooAsset combines both in ManifestTools + ProviderOperation
3. **Our dependency model is bundle-level**: No per-asset DependBundleIDs at runtime (D3 decision); YooAsset tracks asset-level deps
4. **No OperationSystem yet**: Our async operations use Unity coroutines / async directly; time-slicing scheduler deferred to Phase 9
5. **No provider caching layer yet**: Our pool is at the asset level (ref-counting in AAPackageManager._pool); YooAsset has an explicit Provider layer
6. **IPackageBackend is our IFileSystem equivalent**: But less granular - it doesn't split download/cache/buildin concerns

### Adoption Recommendations (future phases)

- **B7 (ABPackageBackend)**: Consider adopting Provider pattern for per-asset async state machine
- **B8 (Handle + ref-count pool)**: Our AssetHandle<T> is already stronger (generics + Result pattern); adopt YooAsset's two-level ref counting (provider + bundle)
- **Phase 9 (H1)**: OperationSystem's time-slicing is valuable for production; consider simplified version
- **General**: YooAsset's mode-agnostic design (5 play modes) is over-engineered for our needs; our 2-backend approach (Addressables + AB) is sufficient
